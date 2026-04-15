#nullable enable

namespace TheDiscDb.Data.Import
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Extracts metadata fields from a TMDB JSON document (movie or series format).
    /// Returns nullable fields so callers can selectively merge only missing data.
    /// </summary>
    public static class TmdbDataExtractor
    {
        private static readonly string[] WriterJobs = { "Screenplay", "Writer", "Story", "Novel" };
        private static readonly string[] ContentRatingCountries = { "US", "GB", "CA" };
        private const int MaxStars = 6;

        public static TmdbMetadata Extract(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new TmdbMetadata();
            }

            using var doc = JsonDocument.Parse(json);
            return Extract(doc.RootElement);
        }

        public static TmdbMetadata Extract(JsonElement root)
        {
            var result = new TmdbMetadata();

            result.DirectorList = GetCrewByJob(root, "Director");
            // For series, fall back to created_by if no directors in crew
            if (result.DirectorList.Count == 0 && root.TryGetProperty("created_by", out var createdBy))
            {
                result.DirectorList = ExtractNames(createdBy);
            }
            result.WriterList = ExtractWriterList(root);
            result.StarList = ExtractStarList(root);
            result.GenreList = ExtractGenreList(root);

            result.Directors = result.DirectorList.Count > 0 ? string.Join(", ", result.DirectorList) : null;
            result.Writers = result.WriterList.Count > 0 ? string.Join(", ", result.WriterList) : null;
            result.Stars = result.StarList.Count > 0 ? string.Join(", ", result.StarList) : null;
            result.Genres = result.GenreList.Count > 0 ? string.Join(", ", result.GenreList) : null;
            result.RuntimeMinutes = ExtractRuntime(root);
            result.ContentRating = ExtractContentRating(root);
            result.Tagline = GetStringProperty(root, "tagline");
            result.Overview = GetStringProperty(root, "overview");

            if (result.RuntimeMinutes.HasValue && result.RuntimeMinutes.Value > 0)
            {
                int hours = result.RuntimeMinutes.Value / 60;
                int minutes = result.RuntimeMinutes.Value % 60;
                result.Runtime = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
            }

            return result;
        }

        private static List<string> ExtractWriterList(JsonElement root)
        {
            var names = new List<string>();
            if (!root.TryGetProperty("credits", out var credits) ||
                credits.ValueKind != JsonValueKind.Object ||
                !credits.TryGetProperty("crew", out var crew) ||
                crew.ValueKind != JsonValueKind.Array)
            {
                return names;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in crew.EnumerateArray())
            {
                var job = GetStringProperty(member, "job");
                var name = GetStringProperty(member, "name");
                if (job != null && name != null &&
                    WriterJobs.Contains(job, StringComparer.OrdinalIgnoreCase) &&
                    seen.Add(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static List<string> ExtractStarList(JsonElement root)
        {
            if (!root.TryGetProperty("credits", out var credits) ||
                credits.ValueKind != JsonValueKind.Object ||
                !credits.TryGetProperty("cast", out var cast) ||
                cast.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return cast.EnumerateArray()
                .OrderBy(c => c.TryGetProperty("order", out var o) ? o.GetInt32() : int.MaxValue)
                .Take(MaxStars)
                .Select(c => GetStringProperty(c, "name"))
                .Where(n => n != null)
                .Cast<string>()
                .ToList();
        }

        private static List<string> ExtractGenreList(JsonElement root)
        {
            if (!root.TryGetProperty("genres", out var genres) ||
                genres.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return genres.EnumerateArray()
                .Select(g => GetStringProperty(g, "name"))
                .Where(n => n != null)
                .Cast<string>()
                .ToList();
        }

        private static int? ExtractRuntime(JsonElement root)
        {
            // Movie format: "runtime" is an integer
            if (root.TryGetProperty("runtime", out var runtime) &&
                runtime.ValueKind == JsonValueKind.Number)
            {
                int value = runtime.GetInt32();
                if (value > 0) return value;
            }

            // Series format: "episode_run_time" can be an array or a single int
            if (root.TryGetProperty("episode_run_time", out var ert))
            {
                if (ert.ValueKind == JsonValueKind.Array)
                {
                    var values = ert.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.Number)
                        .Select(v => v.GetInt32())
                        .ToList();
                    if (values.Count > 0) return values[0];
                }
                else if (ert.ValueKind == JsonValueKind.Number)
                {
                    int value = ert.GetInt32();
                    if (value > 0) return value;
                }
            }

            return null;
        }

        private static string? ExtractContentRating(JsonElement root)
        {
            // Movie format: releases.countries[] with iso_3166_1 and certification
            if (root.TryGetProperty("releases", out var releases) &&
                releases.ValueKind == JsonValueKind.Object &&
                releases.TryGetProperty("countries", out var countries) &&
                countries.ValueKind == JsonValueKind.Array)
            {
                foreach (var country in ContentRatingCountries)
                {
                    var rating = FindCertification(countries, country);
                    if (rating != null) return rating;
                }
            }

            // Series format: content_ratings.results[] with Iso_3166_1 and Rating
            if (root.TryGetProperty("content_ratings", out var contentRatings) &&
                contentRatings.ValueKind == JsonValueKind.Object &&
                contentRatings.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var country in ContentRatingCountries)
                {
                    var rating = FindSeriesRating(results, country);
                    if (rating != null) return rating;
                }
            }

            return null;
        }

        private static string? FindCertification(JsonElement countries, string countryCode)
        {
            foreach (var entry in countries.EnumerateArray())
            {
                var iso = GetStringProperty(entry, "iso_3166_1");
                if (string.Equals(iso, countryCode, StringComparison.OrdinalIgnoreCase))
                {
                    var cert = GetStringProperty(entry, "certification");
                    if (!string.IsNullOrWhiteSpace(cert)) return cert;
                }
            }
            return null;
        }

        private static string? FindSeriesRating(JsonElement results, string countryCode)
        {
            foreach (var entry in results.EnumerateArray())
            {
                // Handle both "iso_3166_1" and "Iso_3166_1" (TMDB API vs cached format)
                var iso = GetStringProperty(entry, "iso_3166_1")
                       ?? GetStringProperty(entry, "Iso_3166_1");
                if (string.Equals(iso, countryCode, StringComparison.OrdinalIgnoreCase))
                {
                    var rating = GetStringProperty(entry, "rating")
                              ?? GetStringProperty(entry, "Rating");
                    if (!string.IsNullOrWhiteSpace(rating)) return rating;
                }
            }
            return null;
        }

        private static List<string> GetCrewByJob(JsonElement root, string job)
        {
            if (!root.TryGetProperty("credits", out var credits) ||
                credits.ValueKind != JsonValueKind.Object ||
                !credits.TryGetProperty("crew", out var crew) ||
                crew.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return crew.EnumerateArray()
                .Where(m => string.Equals(GetStringProperty(m, "job"), job, StringComparison.OrdinalIgnoreCase))
                .Select(m => GetStringProperty(m, "name"))
                .Where(n => n != null)
                .Cast<string>()
                .ToList();
        }

        private static List<string> ExtractNames(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Select(e => GetStringProperty(e, "name"))
                    .Where(n => n != null)
                    .Cast<string>()
                    .ToList();
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var name = GetStringProperty(element, "name");
                return name != null ? new List<string> { name } : new List<string>();
            }
            return new List<string>();
        }

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            return null;
        }
    }

    public class TmdbMetadata
    {
        public string? Directors { get; set; }
        public string? Writers { get; set; }
        public string? Stars { get; set; }
        public string? Genres { get; set; }
        public string? ContentRating { get; set; }
        public int? RuntimeMinutes { get; set; }
        public string? Runtime { get; set; }
        public string? Tagline { get; set; }
        public string? Overview { get; set; }

        /// <summary>Structured list of director names for group creation.</summary>
        public List<string> DirectorList { get; set; } = new();
        /// <summary>Structured list of writer names for group creation.</summary>
        public List<string> WriterList { get; set; } = new();
        /// <summary>Structured list of top-billed cast names for group creation.</summary>
        public List<string> StarList { get; set; } = new();
        /// <summary>Structured list of genre names for group creation.</summary>
        public List<string> GenreList { get; set; } = new();

        /// <summary>
        /// Applies non-null TMDB fields to target, but only when the target field is empty.
        /// </summary>
        public void FillGaps(InputModels.MediaItem target)
        {
            if (string.IsNullOrWhiteSpace(target.Directors) && Directors != null)
                target.Directors = Directors;

            if (string.IsNullOrWhiteSpace(target.Writers) && Writers != null)
                target.Writers = Writers;

            if (string.IsNullOrWhiteSpace(target.Stars) && Stars != null)
                target.Stars = Stars;

            if (string.IsNullOrWhiteSpace(target.Genres) && Genres != null)
                target.Genres = Genres;

            if (string.IsNullOrWhiteSpace(target.ContentRating) && ContentRating != null)
                target.ContentRating = ContentRating;

            if (target.RuntimeMinutes == 0 && RuntimeMinutes.HasValue)
                target.RuntimeMinutes = RuntimeMinutes.Value;

            if (string.IsNullOrWhiteSpace(target.Runtime) && Runtime != null)
                target.Runtime = Runtime;

            if (string.IsNullOrWhiteSpace(target.Tagline) && Tagline != null)
                target.Tagline = Tagline;

            if (string.IsNullOrWhiteSpace(target.Plot) && Overview != null)
                target.Plot = Overview;
        }
    }
}
