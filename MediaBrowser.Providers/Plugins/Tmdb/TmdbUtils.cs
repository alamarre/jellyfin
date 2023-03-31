using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Entities;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;

namespace MediaBrowser.Providers.Plugins.Tmdb
{
    /// <summary>
    /// Utilities for the TMDb provider.
    /// </summary>
    public static class TmdbUtils
    {
        private const int MaxYearDifference = 3;
        private static readonly Regex _nonWords = new(@"[\W_]+", RegexOptions.Compiled);
        private static readonly Regex _fileSystemInvalid = new("[<>:;?*\\/\"]", RegexOptions.Compiled);

        /// <summary>
        /// URL of the TMDb instance to use.
        /// </summary>
        public const string BaseTmdbUrl = "https://www.themoviedb.org/";

        /// <summary>
        /// Name of the provider.
        /// </summary>
        public const string ProviderName = "TheMovieDb";

        /// <summary>
        /// API key to use when performing an API call.
        /// </summary>
        public const string ApiKey = "4219e299c89411838049ab0dab19ebd5";

        /// <summary>
        /// The crew types to keep.
        /// </summary>
        public static readonly string[] WantedCrewTypes =
        {
            PersonType.Director,
            PersonType.Writer,
            PersonType.Producer
        };

        /// <summary>
        /// Cleans the name according to TMDb requirements.
        /// </summary>
        /// <param name="name">The name of the entity.</param>
        /// <returns>The cleaned name.</returns>
        public static string CleanName(string name)
        {
            // TMDb expects a space separated list of words make sure that is the case
            return _nonWords.Replace(name, " ");
        }

        /// <summary>
        /// Maps the TMDb provided roles for crew members to Jellyfin roles.
        /// </summary>
        /// <param name="crew">Crew member to map against the Jellyfin person types.</param>
        /// <returns>The Jellyfin person type.</returns>
        public static string MapCrewToPersonType(Crew crew)
        {
            if (crew.Department.Equals("production", StringComparison.OrdinalIgnoreCase)
                && crew.Job.Contains("director", StringComparison.OrdinalIgnoreCase))
            {
                return PersonType.Director;
            }

            if (crew.Department.Equals("production", StringComparison.OrdinalIgnoreCase)
                && crew.Job.Contains("producer", StringComparison.OrdinalIgnoreCase))
            {
                return PersonType.Producer;
            }

            if (crew.Department.Equals("writing", StringComparison.OrdinalIgnoreCase))
            {
                return PersonType.Writer;
            }

            return string.Empty;
        }

        /// <summary>
        /// Determines whether a video is a trailer.
        /// </summary>
        /// <param name="video">The TMDb video.</param>
        /// <returns>A boolean indicating whether the video is a trailer.</returns>
        public static bool IsTrailerType(Video video)
        {
            return video.Site.Equals("youtube", StringComparison.OrdinalIgnoreCase)
                   && (video.Type.Equals("trailer", StringComparison.OrdinalIgnoreCase)
                       || video.Type.Equals("teaser", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Normalizes a language string for use with TMDb's include image language parameter.
        /// </summary>
        /// <param name="preferredLanguage">The preferred language as either a 2 letter code with or without country code.</param>
        /// <returns>The comma separated language string.</returns>
        public static string GetImageLanguagesParam(string preferredLanguage)
        {
            var languages = new List<string>();

            if (!string.IsNullOrEmpty(preferredLanguage))
            {
                preferredLanguage = NormalizeLanguage(preferredLanguage);

                languages.Add(preferredLanguage);

                if (preferredLanguage.Length == 5) // Like en-US
                {
                    // Currently, TMDb supports 2-letter language codes only.
                    // They are planning to change this in the future, thus we're
                    // supplying both codes if we're having a 5-letter code.
                    languages.Add(preferredLanguage.Substring(0, 2));
                }
            }

            languages.Add("null");

            // Always add English as fallback language
            if (!string.Equals(preferredLanguage, "en", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("en");
            }

            return string.Join(',', languages);
        }

        /// <summary>
        /// Normalizes a language string for use with TMDb's language parameter.
        /// </summary>
        /// <param name="language">The language code.</param>
        /// <returns>The normalized language code.</returns>
        public static string NormalizeLanguage(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return language;
            }

            // TMDb requires this to be uppercase
            // Everything after the hyphen must be written in uppercase due to a way TMDb wrote their API.
            // See here: https://www.themoviedb.org/talk/5119221d760ee36c642af4ad?page=3#56e372a0c3a3685a9e0019ab
            var parts = language.Split('-');

            if (parts.Length == 2)
            {
                // TMDb doesn't support Switzerland (de-CH, it-CH or fr-CH) so use the language (de, it or fr) without country code
                if (string.Equals(parts[1], "CH", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[0];
                }

                language = parts[0] + "-" + parts[1].ToUpperInvariant();
            }

            return language;
        }

        /// <summary>
        /// Adjusts the image's language code preferring the 5 letter language code eg. en-US.
        /// </summary>
        /// <param name="imageLanguage">The image's actual language code.</param>
        /// <param name="requestLanguage">The requested language code.</param>
        /// <returns>The language code.</returns>
        public static string AdjustImageLanguage(string imageLanguage, string requestLanguage)
        {
            if (!string.IsNullOrEmpty(imageLanguage)
                && !string.IsNullOrEmpty(requestLanguage)
                && requestLanguage.Length > 2
                && imageLanguage.Length == 2
                && requestLanguage.StartsWith(imageLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return requestLanguage;
            }

            return imageLanguage;
        }

        /// <summary>
        /// Combines the metadata country code and the parental rating from the API into the value we store in our database.
        /// </summary>
        /// <param name="countryCode">The ISO 3166-1 country code of the rating country.</param>
        /// <param name="ratingValue">The rating value returned by the TMDb API.</param>
        /// <returns>The combined parental rating of country code+rating value.</returns>
        public static string BuildParentalRating(string countryCode, string ratingValue)
        {
            // Exclude US because we store US values as TV-14 without the country code.
            var ratingPrefix = string.Equals(countryCode, "US", StringComparison.OrdinalIgnoreCase) ? string.Empty : countryCode + "-";
            var newRating = ratingPrefix + ratingValue;

            return newRating.Replace("DE-", "FSK-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes invalid characters for file names from the search result name for better comparsison.
        /// </summary>
        /// <param name="searchResultName">The name from the search result.</param>
        /// <returns>The input with invalid file name characters removed.</returns>
        public static string? RemoveInvalidFileCharacters(string? searchResultName)
        {
            if (searchResultName == null)
            {
                return null;
            }

            string formatted = _fileSystemInvalid.Replace(searchResultName, string.Empty);
            return formatted;
        }

        /// <summary>
        /// Identifies the nearest match to the search criteria. Addresses issues with search results from TMDB.
        /// </summary>
        /// <param name="name">The cleaned up name searched for.</param>
        /// <param name="year">The year searched for.</param>
        /// <param name="searchResults">The search results from a Movie or Tv search.</param>
        /// <returns>The nearest match to the search criteria.</returns>
        public static SearchMovieTvBase? GetBestMatch(string name, int year, IReadOnlyList<SearchMovieTvBase> searchResults)
        {
            var filtered = searchResults;

            // partial matches can be returned before exact matches. Look for exact matches after removing invalid characters
            var nearExactMatchFiltered = filtered.Where(s =>
            {
                string? resultName;
                string? resultOriginalName;
                switch (s)
                {
                    case SearchMovie searchMovie:
                        resultName = searchMovie.Title;
                        resultOriginalName = searchMovie.OriginalTitle;
                        break;
                    case SearchTv searchTv:
                        resultName = searchTv.Name;
                        resultOriginalName = searchTv.OriginalName;
                        break;
                    default:
                        return false;
                }

                return string.Equals(RemoveInvalidFileCharacters(resultName), name, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(RemoveInvalidFileCharacters(resultOriginalName), name, StringComparison.OrdinalIgnoreCase);
            });

            if (nearExactMatchFiltered.Any())
            {
                filtered = nearExactMatchFiltered;
            }

            // results of search can sometimes include poor matches (e.g. Hercules (2014) yielding Hercules (1997) as the top result
            if (year > 0)
            {
                var yearFiltered = filtered.Where(s =>
                {
                    DateTime? searchResultYear = (s as SearchMovie)?.ReleaseDate
                        ?? (s as SearchTv)?.FirstAirDate;
                    return searchResultYear.HasValue
                        && Math.Abs(searchResultYear.Value.Year - year) < MaxYearDifference;
                });
                if (yearFiltered.Any())
                {
                    filtered = yearFiltered;
                }
            }

            return filtered.FirstOrDefault();
        }
    }
}
