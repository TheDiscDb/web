using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IMDbApiLib.Models;
using TheDiscDb.InputModels;

namespace TheDiscDb.Data.Import.Pipeline;

/// <summary>
/// Pipeline middleware that creates Group, MediaItemGroup, and ReleaseGroup
/// associations on the ImportItem's MediaItem from IMDB, TMDB, and metadata sources.
/// </summary>
public class GroupMiddleware : IMiddleware
{
    private readonly ConcurrentDictionary<string, Group> groupCache = new(StringComparer.OrdinalIgnoreCase);

    public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

    public async Task Process(ImportItem item, CancellationToken cancellationToken)
    {
        if (item.MediaItem != null)
        {
            AddGroupsFromImdb(item);
            AddGroupsFromTmdb(item);
            AddCustomGroups(item);
        }

        await Next(item, cancellationToken);
    }

    private void AddGroupsFromImdb(ImportItem item)
    {
        var imdb = item.ImdbData;
        if (imdb == null) return;

        if (imdb.GenreList != null)
        {
            foreach (var genre in imdb.GenreList)
            {
                AddMediaItemGroup(item.MediaItem, genre.Value, null, Roles.Genre);
            }
        }

        if (imdb.DirectorList != null)
        {
            AddPeopleGroups(item.MediaItem, imdb.DirectorList, Roles.Director);
        }

        if (imdb.WriterList != null)
        {
            AddPeopleGroups(item.MediaItem, imdb.WriterList, Roles.Writer);
        }

        if (imdb.StarList != null)
        {
            AddPeopleGroups(item.MediaItem, imdb.StarList, Roles.Actor);
        }

        if (imdb.CompanyList != null)
        {
            foreach (var company in imdb.CompanyList)
            {
                AddMediaItemGroup(item.MediaItem, company.Name, company.Id, Roles.Company);
            }
        }
    }

    private void AddGroupsFromTmdb(ImportItem item)
    {
        var tmdb = item.TmdbData;
        if (tmdb == null) return;

        // Only add from TMDB when IMDB data is absent (TMDB fills gaps)
        if (item.ImdbData != null) return;

        foreach (var genre in tmdb.GenreList)
        {
            AddMediaItemGroup(item.MediaItem, genre, null, Roles.Genre);
        }

        foreach (var director in tmdb.DirectorList)
        {
            AddMediaItemGroup(item.MediaItem, director, null, Roles.Director, isFeatured: true);
        }

        foreach (var writer in tmdb.WriterList)
        {
            AddMediaItemGroup(item.MediaItem, writer, null, Roles.Writer);
        }

        foreach (var star in tmdb.StarList)
        {
            AddMediaItemGroup(item.MediaItem, star, null, Roles.Actor);
        }
    }

    private void AddCustomGroups(ImportItem item)
    {
        var metadata = item.Metadata;
        if (metadata?.Groups == null) return;

        foreach (var groupName in metadata.Groups)
        {
            AddMediaItemGroup(item.MediaItem, groupName, null, Roles.CustomGroup);
        }
    }

    private void AddPeopleGroups(MediaItem mediaItem, IEnumerable<StarShort> people, string role)
    {
        foreach (var person in people)
        {
            AddMediaItemGroup(mediaItem, person.Name, person.Id, role,
                isFeatured: role == Roles.Director);
        }
    }

    private void AddMediaItemGroup(MediaItem mediaItem, string name, string? imdbId, string role, bool isFeatured = false)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        // Check if this association already exists on the entity
        bool alreadyExists;
        if (!string.IsNullOrEmpty(imdbId))
        {
            // When we have a stable ID, dedupe by (role + imdbId)
            alreadyExists = mediaItem.MediaItemGroups.Any(mig =>
                mig.Role == role &&
                mig.Group != null &&
                string.Equals(mig.Group.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // Fall back to name-based dedupe when no stable ID exists
            alreadyExists = mediaItem.MediaItemGroups.Any(mig =>
                mig.Role == role &&
                mig.Group != null &&
                string.Equals(mig.Group.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (alreadyExists) return;

        var group = GetOrCreateGroup(name, imdbId);

        mediaItem.MediaItemGroups.Add(new MediaItemGroup
        {
            Group = group,
            MediaItem = mediaItem,
            Role = role,
            IsFeatured = isFeatured
        });
    }

    private Group GetOrCreateGroup(string name, string? imdbId)
    {
        string cacheKey = !string.IsNullOrEmpty(imdbId) ? imdbId : name;

        return groupCache.GetOrAdd(cacheKey, _ => new Group
        {
            Name = name,
            ImdbId = imdbId,
            Slug = name.Slugify()
        });
    }
}
