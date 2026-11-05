using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Requests/Search")]
public sealed class SearchController : ControllerBase
{
    private static TmdbService Tmdb => Plugin.Tmdb;

    [HttpGet("Movie")]
    public async Task<ActionResult<IEnumerable<TmdbSearchResult>>> SearchMovies(
        [FromQuery] string query)
    {
        return await SearchAsync(
            () => Tmdb.SearchMoviesAsync(query),
            query).ConfigureAwait(false);
    }

    [HttpGet("TV")]
    public async Task<ActionResult<IEnumerable<TmdbSearchResult>>> SearchTvShows(
        [FromQuery] string query)
    {
        return await SearchAsync(
            () => Tmdb.SearchTvShowsAsync(query),
            query).ConfigureAwait(false);
    }

    private async Task<ActionResult<IEnumerable<TmdbSearchResult>>> SearchAsync(
        Func<Task<IEnumerable<TmdbSearchResult>>> search,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "The query parameter is required." });
        }

        try
        {
            var results = await search().ConfigureAwait(false);
            return Ok(results);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (TmdbServiceException exception)
        {
            var statusCode = (int)exception.StatusCode switch
            {
                400 => StatusCodes.Status400BadRequest,
                401 => StatusCodes.Status401Unauthorized,
                403 => StatusCodes.Status403Forbidden,
                404 => StatusCodes.Status404NotFound,
                429 => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status502BadGateway
            };

            return StatusCode(statusCode, new { error = exception.Message });
        }
    }
}
