using DiskInspection.Models;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Controllers.PLC
{
 
    class PlcController
    {
        public static bool _firstTrigger = true;
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static Properties.Settings _param = Properties.Settings.Default;

        public static bool ConnectPlc(string url, string ip, int port, int timeout = 1500)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointConnectPlc, Method.Get);
            request.AddQueryParameter("ip", ip);
            request.AddQueryParameter("port", port);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }

        internal static bool DisConnectPlc(string url, int timeout = 1500)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointDisconnectPlc, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
        internal static (TriggerState, bool) CheckTrigger(string url, int timeout = 1500)
        {
            if (_firstTrigger)
            {
                _firstTrigger = false;
                return (TriggerState.OK, true);
            }
            else
            {
                return (TriggerState.OK, false);
            }

            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointReadTrigger, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                if (obj.Success == (int)TriggerState.OK)
                    return (TriggerState.OK, obj.Status);
            }
            return (TriggerState.ERROR, false);
        }
        internal static bool ResetTrigger(string url, int timeout = 1500)
        {

            return true;
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointResetTrigger, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }

        internal static bool CheckPlcConnection(string url, int timeout = 1500)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointCheckConnection, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
        public static bool ControlUv1(string url, bool status, int timeout = 1500)
        {
            // test debug
            return true;
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointControlUv1, Method.Get);
            request.AddQueryParameter("status", status);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
        public static bool ControlUv2(string url, bool status, int timeout = 1500)
        {
            // test debug
            return true;
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointControlUv2, Method.Get);
            request.AddQueryParameter("status", status);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
        public static bool ControlLed1(string url, bool status, int timeout = 1500)
        {
            // test debug
            return true;
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointControlLed1, Method.Get);
            request.AddQueryParameter("status", status);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
        public static bool ControlLed2(string url, bool status, int timeout = 1500)
        {
            // test debug
            return true;
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointControlLed2, Method.Get);
            request.AddQueryParameter("status", status);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
        internal static bool OnError(string url, int timeout = 1500)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointOnError, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic obj = JsonConvert.DeserializeObject(response.Content);
                return obj.Success;
            }
            else
            {
                return false;
            }
        }
    }
}
