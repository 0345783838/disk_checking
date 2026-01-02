using DiskInspection.Models;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Controllers.APIs
{
    class APICommunication
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static Properties.Settings _param = Properties.Settings.Default;


        public static DebugImageResponse DebugImages(string url, List<string> imagePaths)
        {
            dynamic obj = new DebugImageResponse();
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(40000)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndPointDebug, Method.Post);
            request.AddHeader("accept", "application/json");

            // Tạo payload JSON
            var payload = new
            {
                image_paths = imagePaths
            };
            request.AddJsonBody(payload);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {

                    obj = JsonConvert.DeserializeObject<DebugImageResponse>(response.Content);
                    if (obj.Status)
                        return obj;
                    else return null;

                }
                catch (Exception ex)
                {
                    logger.Debug(ex.Message);
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

    }
}
