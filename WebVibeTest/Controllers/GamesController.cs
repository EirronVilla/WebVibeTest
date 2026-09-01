using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebVibeTest.Application.Games;
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
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceSettlement(Guid id, int vertexId, CancellationToken cancellationToken)
    {
        try
        {
            await gameService.PlaceInitialSettlementAsync(CurrentUserId, id, vertexId, cancellationToken);
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
}
