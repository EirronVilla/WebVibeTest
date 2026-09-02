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
    IHubContext<GameHub> hubContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var games = await gameService.GetAvailablePublicGamesAsync(CurrentUserId, cancellationToken);
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
            .ToList()
        });
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateGameViewModel());

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
            var clients = hubContext.Clients.Group(GameHub.GroupName(id));
            await clients.SendAsync(GameHub.DiceRolledEvent, new { gameId = id, result.Die1, result.Die2 }, cancellationToken);
            if (result.Production.Count > 0)
            {
                await clients.SendAsync(GameHub.ResourceProductionEvent, new { gameId = id, result.Production }, cancellationToken);
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
            await SendTradeEventAsync(GameHub.TradeOfferedEvent, id, result, cancellationToken);
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
            await SendTradeEventAsync(GameHub.TradeRespondedEvent, id, result, cancellationToken);
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
        try { await BroadcastBuildAsync(id, await gameService.BuildFreeRoadAsync(CurrentUserId, id, edgeId, cancellationToken), cancellationToken); return Ok(); }
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
        hubContext.Clients.Group(GameHub.GroupName(gameId))
            .SendAsync(GameHub.GameStateUpdatedEvent, gameId, cancellationToken);

    private async Task BroadcastBuildAsync(Guid gameId, BuildResult result, CancellationToken cancellationToken)
    {
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
            var result = await play();
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
