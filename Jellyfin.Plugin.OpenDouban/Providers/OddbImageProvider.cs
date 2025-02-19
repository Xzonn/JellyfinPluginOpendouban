using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenDouban.Providers
{
    /// <summary>
    /// OddbImageProvider.
    /// </summary>
    public class OddbImageProvider : IRemoteImageProvider
    {
        private ILogger<OddbImageProvider> _logger;
        private IHttpClientFactory _httpClientFactory;
        private OddbApiClient _oddbApiClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="OddbImageProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{OddbImageProvider}"/> interface.</param>
        /// <param name="oddbApiClient">Instance of <see cref="OddbApiClient"/>.</param>
        public OddbImageProvider(IHttpClientFactory httpClientFactory, ILogger<OddbImageProvider> logger, OddbApiClient oddbApiClient)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _oddbApiClient = oddbApiClient;
        }

        /// <inheritdoc />
        public string Name => OddbPlugin.ProviderName;

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Movie || item is Series || item is Season;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>
        {
            ImageType.Primary,
            ImageType.Backdrop
        };

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"[OpenDouban] GetImages for item: {item.Name}");

            string sid = item.GetProviderId(OddbPlugin.ProviderId);

            if (string.IsNullOrEmpty(sid))
            {
                _logger.LogWarning($"[OpenDouban] Got images failed because the sid of \"{item.Name}\" is empty!");
                return new List<RemoteImageInfo>();
            }

            var parameters = new Dictionary<string, string>() { { "s", OddbPlugin.Instance?.Configuration.PosterSize } };
            var primary = await _oddbApiClient.GetBySidWithParams(sid, parameters, cancellationToken);
            var dropback = await GetBackdrop(sid, cancellationToken);

            var res = new List<RemoteImageInfo> {
                new RemoteImageInfo
                {
                    Language = "zh",
                    ProviderName = "Douban",
                    Url = primary.Img,
                    Type = ImageType.Primary
                }
            };
            res.AddRange(dropback);
            return res;
        }

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[OpenDouban] GetImageResponse url: {0}", url);
            HttpResponseMessage response = await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
        }

        /// <summary>
        /// Query for a background photo
        /// </summary>
        /// <param name="sid">a subject/movie id</param>
        /// <param name="cancellationToken">Instance of the <see cref="CancellationToken"/> interface.</param>
        public async Task<IEnumerable<RemoteImageInfo>> GetBackdrop(string sid, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[OpenDouban] GetBackdrop of sid: {0}", sid);
            var photo = await _oddbApiClient.GetPhotoBySid(sid, cancellationToken);
            var list = new List<RemoteImageInfo>();

            if (photo == null)
            {
                return list;
            }

            return photo.Where(x => x.Width > x.Height * 1.3).Select(x =>
            {
                return new RemoteImageInfo
                {
                    Language = "zh",
                    ProviderName = "Douban",
                    Url = x.Large,
                    Type = ImageType.Backdrop,
                };
            });
        }
    }
}
