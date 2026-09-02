using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebVibeTest.Application.Games;
using WebVibeTest.Domain.Games;
using WebVibeTest.Hubs;
using WebVibeTest.Models.Games;

namespace WebVibeTest.Controllers;

[Authorize]
public sealed class GamesController(
    IGameService gameService,
    UserManager<IdentityUser> userManager,
    IHubContext<GameHub> hubContext,
    IGameActionLog actionLog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var games = await gameService.GetAvailablePublicGamesAsync(CurrentUserId, cancellationToken);
        var activeGames = await gameService.GetActiveGamesAsync(CurrentUserId, cancellationToken);
        return View(new GamesIndexViewModel
        {
            Games = games.Select(game => new AvailableGameViewModel
            {
                Id = game.Id,
                Name = game.Name,
                HostName = game.HostName,
                PlayerCount = game.PlayerCount,
                MaxPlayers = game.MaxPlayers,
                IsMember = game.IsMember
            })
            .ToList(),
            ActiveGames = activeGames.Select(game => new ActiveGameViewModel
            {
                Id = game.Id,
                Name = game.Name,
                HostName = game.HostName,
                PlayerCount = game.PlayerCount,
                StartedAtUtc = game.StartedAtUtc,
                IsCurrentUserHost = game.IsCurrentUserHost
            }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelActive(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.CancelActiveGameAsync(CurrentUserId, id, cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id)).SendAsync(GameHub.GameCancelledEvent, id, cancellationToken);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateGameViewModel());

    [HttpGet]
    public async Task<IActionResult> Statistics(CancellationToken cancellationToken) => View(await gameService.GetStatisticsAsync(CurrentUserId, cancellationToken));

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Completed(Guid id, CancellationToken cancellationToken) => View(await gameService.GetCompletedGamePublicAsync(id, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGameViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var game = await gameService.CreateGameAsync(
                CurrentUserId,
                model.Name,
                model.MaxPlayers,
                model.IsPrivate,
                cancellationToken);
            return RedirectToAction(nameof(Lobby), new { id = game.Id });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinPublic(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var player = await gameService.JoinPublicGameAsync(CurrentUserId, id, cancellationToken);
            await NotifyLobbyChangedAsync(player.GameId, cancellationToken);
            return RedirectToAction(nameof(Lobby), new { id = player.GameId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinPrivate(JoinPrivateGameViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter a valid 12-character join code.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var player = await gameService.JoinPrivateGameAsync(CurrentUserId, model.JoinCode, cancellationToken);
            await NotifyLobbyChangedAsync(player.GameId, cancellationToken);
            return RedirectToAction(nameof(Lobby), new { id = player.GameId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Lobby(Guid id, CancellationToken cancellationToken)
    {
        WaitingLobby lobby;
        try
        {
            lobby = await gameService.GetWaitingLobbyAsync(CurrentUserId, id, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // SignalR can cause the browser to replace this request while the
            // lobby is being refreshed. An abandoned read is not an app error.
            return new EmptyResult();
        }

        var model = new LobbyViewModel
        {
            Id = lobby.Id,
            Name = lobby.Name,
            HostName = lobby.HostName,
            MaxPlayers = lobby.MaxPlayers,
            IsPrivate = lobby.IsPrivate,
            JoinCode = lobby.JoinCode,
            IsCurrentUserHost = lobby.IsCurrentUserHost,
            CanStart = lobby.CanStart,
            ColorsAreUnique = lobby.ColorsAreUnique,
            Players = lobby.Players
                .Select(player => new LobbyPlayerViewModel
                {
                    DisplayName = player.DisplayName,
                    Color = player.Color,
                    IsHost = player.IsHost,
                    IsCurrentUser = player.IsCurrentUser
                })
                .ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectColor(Guid id, PlayerColor color, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.SelectPlayerColorAsync(CurrentUserId, id, color, cancellationToken);
            await NotifyLobbyChangedAsync(id, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(Lobby), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.StartGameAsync(CurrentUserId, id, cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id))
                .SendAsync(GameHub.GameStartedEvent, id, cancellationToken);
            return RedirectToAction(nameof(Play), new { id });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Lobby), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Play(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await gameService.GetActiveGameAsync(CurrentUserId, id, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            try { return View("Completed", await gameService.GetCompletedGameAsync(CurrentUserId, id, cancellationToken)); }
            catch (InvalidOperationException) { }
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser can abandon this read when a SignalR update replaces the page.
            // Request cancellation is expected and should not surface as an application error.
            return new EmptyResult();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceSettlement(Guid id, int vertexId, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.PlaceInitialSettlementAsync(CurrentUserId, id, vertexId, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.SettlementBuilt, CurrentUserId), cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id)).SendAsync(GameHub.AwardsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceRoad(Guid id, int edgeId, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.PlaceInitialRoadAsync(CurrentUserId, id, edgeId, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.RoadBuilt, CurrentUserId), cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id)).SendAsync(GameHub.AwardsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RollDice(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.RollDiceAsync(CurrentUserId, id, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.DiceRolled, CurrentUserId, DiceTotal: result.Die1 + result.Die2), cancellationToken);
            var clients = hubContext.Clients.Group(GameHub.GroupName(id));
            await clients.SendAsync(GameHub.DiceRolledEvent, new { gameId = id, result.Die1, result.Die2 }, cancellationToken);
            if (result.Production.Count > 0)
            {
                await clients.SendAsync(
                    GameHub.ResourceProductionEvent,
                    new
                    {
                        gameId = id,
                        Production = result.Production.Select(item => new { item.UserId, item.CardsProduced })
                    },
                    cancellationToken);
                foreach (var production in result.Production)
                {
                    await hubContext.Clients.User(production.UserId).SendAsync(
                        GameHub.ResourceCardsReceivedEvent,
                        new { gameId = id, production.Resources },
                        cancellationToken);
                }
            }
            await clients.SendAsync(GameHub.ResourceCountsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(
        Guid id, int brick, int lumber, int wool, int grain, int ore, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.DiscardResourcesAsync(
                CurrentUserId,
                id,
                new ResourceDiscard(brick, lumber, wool, grain, ore),
                cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.CardsDiscarded, CurrentUserId, Quantity: brick + lumber + wool + grain + ore), cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id))
                .SendAsync(GameHub.ResourceCountsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveRobber(Guid id, int hexId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.MoveRobberAsync(CurrentUserId, id, hexId, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.RobberMoved, CurrentUserId), cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id))
                .SendAsync(GameHub.RobberMovedEvent, new { gameId = id, result.HexId }, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RobPlayer(Guid id, string targetUserId, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.RobPlayerAsync(CurrentUserId, id, targetUserId, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.PlayerRobbed, CurrentUserId, TargetUserId: targetUserId), cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id))
                .SendAsync(GameHub.ResourceCountsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndTurn(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.EndTurnAsync(CurrentUserId, id, cancellationToken);
            var clients = hubContext.Clients.Group(GameHub.GroupName(id));
            foreach (var offerId in result.CancelledTradeOfferIds)
                await clients.SendAsync(GameHub.TradeCancelledEvent, new { gameId = id, offerId }, cancellationToken);
            await clients.SendAsync(GameHub.TurnChangedEvent, new { gameId = id, result.CurrentPlayerUserId }, cancellationToken);
            await clients.SendAsync(GameHub.PairedTurnChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuildRoad(Guid id, int edgeId, CancellationToken cancellationToken)
    {
        try
        {
            await actionLog.CaptureAwardsAsync(id, cancellationToken);
            var result = await gameService.BuildRoadAsync(CurrentUserId, id, edgeId, cancellationToken);
            await BroadcastBuildAsync(id, result, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuildSettlement(Guid id, int vertexId, CancellationToken cancellationToken)
    {
        try
        {
            await actionLog.CaptureAwardsAsync(id, cancellationToken);
            var result = await gameService.BuildSettlementAsync(CurrentUserId, id, vertexId, cancellationToken);
            await BroadcastBuildAsync(id, result, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuildCity(Guid id, int vertexId, CancellationToken cancellationToken)
    {
        try
        {
            await actionLog.CaptureAwardsAsync(id, cancellationToken);
            var result = await gameService.BuildCityAsync(CurrentUserId, id, vertexId, cancellationToken);
            await BroadcastBuildAsync(id, result, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProposeTrade(
        Guid id,
        int offeredBrick, int offeredLumber, int offeredWool, int offeredGrain, int offeredOre,
        int requestedBrick, int requestedLumber, int requestedWool, int requestedGrain, int requestedOre,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.ProposeTradeAsync(
                CurrentUserId, id,
                new ResourceBundle(offeredBrick, offeredLumber, offeredWool, offeredGrain, offeredOre),
                new ResourceBundle(requestedBrick, requestedLumber, requestedWool, requestedGrain, requestedOre),
                cancellationToken);
            await hubContext.Clients.Users(result.ParticipantUserIds).SendAsync(GameHub.TradeOfferedEvent, new
            {
                gameId = id,
                offerId = result.OfferId,
                proposerUserId = CurrentUserId,
                responseDeadlineUtc = result.ResponseDeadlineUtc,
                offered = new { brick = offeredBrick, lumber = offeredLumber, wool = offeredWool, grain = offeredGrain, ore = offeredOre },
                requested = new { brick = requestedBrick, lumber = requestedLumber, wool = requestedWool, grain = requestedGrain, ore = requestedOre }
            }, cancellationToken);
            await hubContext.Clients.User(CurrentUserId).SendAsync(GameHub.GameStateUpdatedEvent, id, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondToTrade(Guid id, Guid offerId, bool accept, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.RespondToTradeAsync(CurrentUserId, id, offerId, accept, cancellationToken);
            if (result.AllRejected)
                if (result.ProposerUserId is not null)
                    await hubContext.Clients.User(result.ProposerUserId).SendAsync(GameHub.TradeAllRejectedEvent, new { gameId = id, offerId }, cancellationToken);
            if (result.AllResponded && result.AcceptedUserIds?.Count > 0 && result.ProposerUserId is not null)
            {
                var acceptedNames = new Dictionary<string, string>();
                foreach (var acceptedId in result.AcceptedUserIds)
                    acceptedNames[acceptedId] = await userManager.GetUserNameAsync(await userManager.FindByIdAsync(acceptedId) ?? new IdentityUser { Id = acceptedId }) ?? acceptedId;
                await hubContext.Clients.User(result.ProposerUserId).SendAsync(GameHub.TradeReadyEvent, new { gameId = id, offerId, acceptedUserIds = result.AcceptedUserIds, acceptedNames }, cancellationToken);
            }
            await hubContext.Clients.Users(result.ParticipantUserIds).SendAsync(GameHub.TradeRespondedEvent, new { gameId = id, result.OfferId, accept }, cancellationToken);
            // Refresh after every response. Besides keeping response status current,
            // this lets the persisted offer state recover the UI if a targeted
            // TradeReady/TradeAllRejected notification is ever missed.
            await hubContext.Clients.Group(GameHub.GroupName(id))
                .SendAsync(GameHub.GameStateUpdatedEvent, id, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return BadRequest(exception.Message); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeTrade(Guid id, Guid offerId, string acceptingUserId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.FinalizeTradeAsync(CurrentUserId, id, offerId, acceptingUserId, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.PlayerTradeCompleted, CurrentUserId, TargetUserId: acceptingUserId, TradeOfferId: offerId), cancellationToken);
            await SendTradeEventAsync(GameHub.TradeCompletedEvent, id, result, cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id)).SendAsync(GameHub.ResourceCountsChangedEvent, id, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelTrade(Guid id, Guid offerId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.CancelTradeAsync(CurrentUserId, id, offerId, cancellationToken);
            await SendTradeEventAsync(GameHub.TradeCancelledEvent, id, result, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MaritimeTrade(Guid id, ResourceType give, ResourceType receive, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.MaritimeTradeAsync(CurrentUserId, id, give, receive, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.MaritimeTradeCompleted, CurrentUserId, GivenResource: result.Given, ReceivedResource: result.Received, TradeRate: result.Rate), cancellationToken);
            var clients = hubContext.Clients.Group(GameHub.GroupName(id));
            await clients.SendAsync(GameHub.MaritimeTradeCompletedEvent, new { gameId = id, result.Given, result.Rate, result.Received }, cancellationToken);
            await clients.SendAsync(GameHub.ResourceCountsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return BadRequest(exception.Message); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BuyDevelopmentCard(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gameService.BuyDevelopmentCardAsync(CurrentUserId, id, cancellationToken);
            await actionLog.RecordAsync(new GameActionEvent(id, GameActionKind.DevelopmentCardBought, CurrentUserId), cancellationToken);
            await hubContext.Clients.User(result.OwnerUserId).SendAsync(GameHub.DevelopmentCardBoughtEvent,
                new { gameId = id, result.CardId, result.Type }, cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(id)).SendAsync(GameHub.ResourceCountsChangedEvent, id, cancellationToken);
            await NotifyGameStateChangedAsync(id, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return BadRequest(exception.Message); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> PlayKnight(Guid id, Guid cardId, CancellationToken cancellationToken) =>
        PlayDevelopmentCardAsync(id, () => gameService.PlayKnightAsync(CurrentUserId, id, cardId, cancellationToken), cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> PlayRoadBuilding(Guid id, Guid cardId, CancellationToken cancellationToken) =>
        PlayDevelopmentCardAsync(id, () => gameService.PlayRoadBuildingAsync(CurrentUserId, id, cardId, cancellationToken), cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> PlayYearOfPlenty(Guid id, Guid cardId, ResourceType first, ResourceType second, CancellationToken cancellationToken) =>
        PlayDevelopmentCardAsync(id, () => gameService.PlayYearOfPlentyAsync(CurrentUserId, id, cardId, first, second, cancellationToken), cancellationToken, true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> PlayMonopoly(Guid id, Guid cardId, ResourceType resource, CancellationToken cancellationToken) =>
        PlayDevelopmentCardAsync(id, () => gameService.PlayMonopolyAsync(CurrentUserId, id, cardId, resource, cancellationToken), cancellationToken, true);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BuildFreeRoad(Guid id, int edgeId, CancellationToken cancellationToken)
    {
        try { await actionLog.CaptureAwardsAsync(id, cancellationToken); await BroadcastBuildAsync(id, await gameService.BuildFreeRoadAsync(CurrentUserId, id, edgeId, cancellationToken), cancellationToken); return Ok(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return BadRequest(exception.Message); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FinishRoadBuilding(Guid id, CancellationToken cancellationToken)
    {
        try { await gameService.FinishRoadBuildingAsync(CurrentUserId, id, cancellationToken); await NotifyGameStateChangedAsync(id, cancellationToken); return Ok(); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.LeaveWaitingGameAsync(CurrentUserId, id, cancellationToken);
            await NotifyLobbyChangedAsync(id, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private string CurrentUserId => userManager.GetUserId(User)
        ?? throw new UnauthorizedAccessException("An authenticated user is required.");

    private Task NotifyLobbyChangedAsync(Guid gameId, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(GameHub.GroupName(gameId))
            .SendAsync(GameHub.LobbyUpdatedEvent, gameId, cancellationToken);

    private Task NotifyGameStateChangedAsync(Guid gameId, CancellationToken cancellationToken) =>
        NotifyGameStateAndCompletionAsync(gameId, cancellationToken);

    private async Task NotifyGameStateAndCompletionAsync(Guid gameId, CancellationToken cancellationToken)
    {
        var clients = hubContext.Clients.Group(GameHub.GroupName(gameId));
        var isCompleted = await gameService.IsCompletedAsync(gameId, cancellationToken);
        if (isCompleted) await actionLog.RecordCompletionAsync(gameId, cancellationToken);
        await clients.SendAsync(GameHub.GameStateUpdatedEvent, gameId, cancellationToken);
        if (isCompleted)
        {
            await clients.SendAsync(GameHub.GameCompletedEvent, gameId, cancellationToken);
        }
    }

    private async Task BroadcastBuildAsync(Guid gameId, BuildResult result, CancellationToken cancellationToken)
    {
        var kind = result.BuildingType switch
        {
            "Road" => GameActionKind.RoadBuilt,
            "Settlement" => GameActionKind.SettlementBuilt,
            "City" => GameActionKind.CityBuilt,
            _ => throw new InvalidOperationException($"Unknown building type '{result.BuildingType}'.")
        };
        await actionLog.RecordAsync(new GameActionEvent(gameId, kind, result.UserId), cancellationToken);
        await actionLog.RecordAwardChangesAsync(gameId, cancellationToken);
        var clients = hubContext.Clients.Group(GameHub.GroupName(gameId));
        await clients.SendAsync(GameHub.BuildingPlacedEvent, new
        {
            gameId,
            result.BuildingType,
            result.LocationId,
            result.UserId
        }, cancellationToken);
        await clients.SendAsync(GameHub.ResourceCountsChangedEvent, gameId, cancellationToken);
        await clients.SendAsync(GameHub.AwardsChangedEvent, gameId, cancellationToken);
        await NotifyGameStateChangedAsync(gameId, cancellationToken);
    }

    private async Task SendTradeEventAsync(
        string eventName, Guid gameId, TradeEventResult result, CancellationToken cancellationToken)
    {
        await hubContext.Clients.Users(result.ParticipantUserIds)
            .SendAsync(eventName, new { gameId, result.OfferId }, cancellationToken);
        await NotifyGameStateChangedAsync(gameId, cancellationToken);
    }

    private async Task<IActionResult> PlayDevelopmentCardAsync(
        Guid gameId, Func<Task<DevelopmentCardPlayResult>> play, CancellationToken cancellationToken, bool resourcesChanged = false)
    {
        try
        {
            await actionLog.CaptureAwardsAsync(gameId, cancellationToken);
            var result = await play();
            await actionLog.RecordAsync(new GameActionEvent(gameId, GameActionKind.DevelopmentCardPlayed, result.PlayerUserId, DevelopmentCardType: result.Type), cancellationToken);
            await actionLog.RecordAwardChangesAsync(gameId, cancellationToken);
            var clients = hubContext.Clients.Group(GameHub.GroupName(gameId));
            await clients.SendAsync(GameHub.DevelopmentCardPlayedEvent,
                new { gameId, result.Type, result.PlayerUserId }, cancellationToken);
            await clients.SendAsync(GameHub.AwardsChangedEvent, gameId, cancellationToken);
            if (resourcesChanged) await clients.SendAsync(GameHub.ResourceCountsChangedEvent, gameId, cancellationToken);
            await NotifyGameStateChangedAsync(gameId, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return BadRequest(exception.Message); }
    }
}
