using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.OpenDouban.Providers
{
    /// <summary>
    /// OddbSeriesProvider.
    /// </summary>
    public class OddbSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        private ILogger<OddbSeriesProvider> _logger;
        private IHttpClientFactory _httpClientFactory;
        private OddbApiClient _oddbApiClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="OddbSeriesProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{OddbSeriesProvider}"/> interface.</param>
        /// <param name="oddbApiClient">Instance of <see cref="OddbApiClient"/>.</param>
        public OddbSeriesProvider(IHttpClientFactory httpClientFactory, ILogger<OddbSeriesProvider> logger, OddbApiClient oddbApiClient)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _oddbApiClient = oddbApiClient;
        }

        /// <inheritdoc />
        public string Name => OddbPlugin.ProviderName;

        /// <summary>
        /// Pattern for media name filtering
        /// </summary>
        private string _pattern;
        public string Pattern
        {
            get
            {
                if (string.IsNullOrEmpty(_pattern))
                {
                    return OddbPlugin.Instance?.Configuration.Pattern;
                }
                return _pattern;
            }
            set
            {
                _pattern = value;
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        {
            List<ApiSubject> list = new List<ApiSubject>();

            string sid = info.GetProviderId(OddbPlugin.ProviderId);
            if (!string.IsNullOrEmpty(sid))
            {
                _logger.LogInformation($"[OpenDouban] Series GetSearchResults of [sid]: \"{sid}\"");
                ApiSubject res = await _oddbApiClient.GetBySid(sid, cancellationToken);
                list.Add(res);
            }
            else if (!string.IsNullOrEmpty(info.Name))
            {
                _logger.LogInformation($"[OpenDouban] Series GetSearchResults of [name]: \"{info.Name}\"");
                List<ApiSubject> res = await _oddbApiClient.PartialSearch(info.Name, cancellationToken);
                list.AddRange(res);
            }

            if (!list.Any())
            {
                _logger.LogInformation($"[OpenDouban] Series GetSearchResults Found Nothing...");
            }

            return list.Select(x =>
            {
                // int year = 0; int.TryParse(x?.Year, out year);
                return new RemoteSearchResult
                {
                    ProviderIds = new Dictionary<string, string> { { OddbPlugin.ProviderId, x.Sid } },
                    ImageUrl = x?.Img,
                    ProductionYear = x?.Year,
                    Name = x?.Name
                };
            });
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            ApiSubject subject = null;

            string sid = info.GetProviderId(OddbPlugin.ProviderId);

            if (!string.IsNullOrEmpty(sid))
            {
                _logger.LogInformation($"[OpenDouban] Series GetMetadata of [sid]: \"{sid}\"");
                subject = await _oddbApiClient.GetBySid(sid, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(info.Name))
            {
                string name = Regex.Replace(info.Name, Pattern, " ");
                _logger.LogInformation($"[OpenDouban] Series GetMetadata of [name]: \"{name}\"");

                List<ApiSubject> res = await _oddbApiClient.PartialSearch(name, cancellationToken);

                // Getting 1st item from the result
                var has = res;
                if (has.Any())
                {
                    sid = has.FirstOrDefault().Sid;
                    subject = await _oddbApiClient.GetBySid(sid, cancellationToken);
                }
            }

            var result = new MetadataResult<Series>();
            if (subject == null) return result;

            var x = subject;

            result.Item = new Series
            {
                ProviderIds = new Dictionary<string, string> { { OddbPlugin.ProviderId, x.Sid } },
                Name = x?.Name,
                OriginalTitle = x?.OriginalName,
                CommunityRating = x?.Rating,
                Overview = x?.Intro,
                ProductionYear = x?.Year,
                HomePageUrl = x.Site,
                Genres = x?.Genre.Split("/").Select(x => x.Trim()).ToArray(),
                ProductionLocations = x?.Country.Split("/").Select(x => x.Trim()).ToArray(),
                PremiereDate = x?.ScreenTime,
            };

            info.SetProviderId(OddbPlugin.ProviderId, x.Sid);
            if (!string.IsNullOrEmpty(x.Imdb))
            {
                info.SetProviderId(MetadataProvider.Imdb, x.Imdb);
            }

            result.QueriedById = true;
            result.HasMetadata = true;

            x.Celebrities = await _oddbApiClient.GetCelebritiesBySid(sid, cancellationToken);

            x.Celebrities.ForEach(c => result.AddPerson(new MediaBrowser.Controller.Entities.PersonInfo
            {
                Name = c.Name,
                Type = c.Role switch
                {
                    "导演" => PersonType.Director,
                    "演员" => PersonType.Actor,
                    "配音" => PersonType.Actor,
                    "编剧" => PersonType.Writer,
                    "制片人" => PersonType.Producer,
                    "作曲" => PersonType.Composer,
                    _ => PersonType.Actor,
                },
                Role = c.Rolename,
                ImageUrl = c.Img,
                ProviderIds = new Dictionary<string, string> { { OddbPlugin.ProviderId, c.Id } },
            }));

            return result;
        }

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[OpenDouban] GetImageResponse url: {0}", url);
            HttpResponseMessage response = await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
        }
    }
}
