using DiskInspection.Models;
using Emgu.CV;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskInspection.Controllers.APIs
{
    class APICommunication
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static Properties.Settings _param = Properties.Settings.Default;


        public static DebugImageResponse DebugImages(string url,Mat image, EnvironmentConfig envConfig, int timeout=10000)
        {
            dynamic obj = new DebugImageResponse();
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndPointDebug, Method.Post);
            request.AlwaysMultipartFormData = true;

            // Add File
            byte[] jpegData = image.ToImage<Bgr, byte>().ToJpegData();
            request.AddFile("image", jpegData, $"image.jpg");

            // Tạo payload JSON
            var payload = new
            {
                segment_threshold = envConfig.SegmentThreshold,
                detect_threshold = envConfig.DetectThreshold,
                detect_iou = envConfig.DetectIou,
                caliper_min_edge_distance = envConfig.CaliperMinEdgeDistance,
                caliper_max_edge_distance = envConfig.CaliperMaxEdgeDistance,
                caliper_length_rate = envConfig.CaliperLengthRate,
                caliper_thickness_list = envConfig.CaliperThicknessList,
                disk_num = envConfig.DiskNumber,
                disk_max_distance = envConfig.DiskMaxDistance,
                disk_min_distance = envConfig.DiskMinDistance,
                disk_min_area = envConfig.DiskMinArea
            };
            string paramsJson = JsonConvert.SerializeObject(payload);
            request.AddParameter(
                                "params_json",
                                paramsJson,
                                ParameterType.GetOrPost
            );

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    
                    obj = JsonConvert.DeserializeObject<DebugImageResponse>(response.Content);
                    return obj;
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
        public static DebugImageResponse DebugUvImages(string url, Mat image, EnvironmentConfig envConfig, int timeout = 10000)
        {
            dynamic obj = new DebugImageResponse();
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndPointDebugUv, Method.Post);
            request.AlwaysMultipartFormData = true;

            // Add File
            byte[] jpegData = image.ToImage<Bgr, byte>().ToJpegData();
            request.AddFile("image", jpegData, $"image.jpg");

            // Tạo payload JSON
            var payload = new
            {
                segment_threshold = envConfig.SegmentThreshold,
                detect_threshold = envConfig.DetectThreshold,
                detect_iou = envConfig.DetectIou,
                caliper_min_edge_distance = envConfig.CaliperMinEdgeDistance,
                caliper_max_edge_distance = envConfig.CaliperMaxEdgeDistance,
                caliper_length_rate = envConfig.CaliperLengthRate,
                caliper_thickness_list = envConfig.CaliperThicknessList,
                disk_num = envConfig.DiskNumber,
                disk_max_distance = envConfig.DiskMaxDistance,
                disk_min_distance = envConfig.DiskMinDistance,
                disk_min_area = envConfig.DiskMinArea
            };
            string paramsJson = JsonConvert.SerializeObject(payload);
            request.AddParameter(
                                "params_json",
                                paramsJson,
                                ParameterType.GetOrPost
            );

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {

                    obj = JsonConvert.DeserializeObject<DebugImageResponse>(response.Content);
                    return obj;
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
        public static InspectionResponse InspectWhiteLight(string url, Mat image, int timeout = 10000) 
        {
            dynamic obj = new InspectionResponse();
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointInspectWhiteLight, Method.Post);
            request.AlwaysMultipartFormData = true;
            byte[] jpegData = image.ToImage<Bgr, byte>().ToJpegData();
            request.AddFile("image", jpegData, $"image.jpg");
            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {

                    obj = JsonConvert.DeserializeObject<InspectionResponse>(response.Content);
                    return obj;

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
        internal static InspectionResponse InspectUvLight(string url, Mat image, int timeout = 10000)
        {
            dynamic obj = new InspectionResponse();
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointInspectUvLight, Method.Post);
            request.AlwaysMultipartFormData = true;
            byte[] jpegData = image.ToImage<Bgr, byte>().ToJpegData();
            request.AddFile("image", jpegData, $"image.jpg");
            var response = client.Execute(request);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {

                    obj = JsonConvert.DeserializeObject<InspectionResponse>(response.Content);
                    return obj;

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
        public static bool CheckAPIStatus(string url, int timeout = 1000)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndPointCheckStatus, Method.Get);

            var response = client.Execute(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
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
        public static bool ControlUv(string url, bool status, int timeout = 1500)
        {
            var options = new RestClientOptions(url)
            {
                Timeout = TimeSpan.FromMilliseconds(timeout)
            };
            var client = new RestClient(options);
            var request = new RestRequest(_param.EndpointControlUv, Method.Get);
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
