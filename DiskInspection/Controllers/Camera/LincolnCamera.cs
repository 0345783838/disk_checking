using System;
using System.Collections;
using MvCamCtrl.NET;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows;

namespace DiskInspection.Controllers.Camera
{
    class LincolnCamera
    {
        private static Object synLock = new Object();
        private static NLog.Logger logger = NLog.LogManager.GetLogger("debug");
        private static ArrayList _ListCamInfo;
        private MyCamera cam;
        public string SN = "";
        private static MyCamera.MV_CC_DEVICE_INFO_LIST m_pDeviceList;
        private UInt32 m_nBufSizeForDriver = 3072 * 2048 * 3;
        private byte[] m_pBufForDriver = new byte[3072 * 2048 * 3];
        private UInt32 m_nBufSizeForSaveImage = 3072 * 2048 * 3 * 3 + 2048;
        private byte[] m_pBufForSaveImage = new byte[3072 * 2048 * 3 * 3 + 2048];
        public LincolnCamera(string SN)
        {

            ListDevices();
            this.SN = SN;
            GetDeviceBySN(SN);
        }
        public LincolnCamera(int idx)
        {
            ListDevices();
            GetDeviceByIdx(idx);
            if (idx < _ListCamInfo.Count)
            {
                CamInfo info = (CamInfo)_ListCamInfo[idx];
                SN = info.SN;
            }

        }
        public bool IsOpen()
        {
            return cam == null ? false : true;
        }

        public static ArrayList GetListCamInfo()
        {
            ListDevices();
            return _ListCamInfo;
        }
        public void SetExposureTime(long value)
        {
            int ret = cam.MV_CC_SetFloatValue_NET("ExposureTime", (float)value);
        }

        public bool Start()
        {
            int nRet;
            nRet = cam.MV_CC_StartGrabbing_NET();
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("Trigger Fail!", nRet);
                return false;
            }
            return true;
        }

        public bool Stop()
        {
            int nRet = -1;
            nRet = cam.MV_CC_StopGrabbing_NET();
            if (nRet != MyCamera.MV_OK)
            {
                logger.Error("Stop Grabbing Fail!");
                return false;
            }
            return true;
        }
        private void GetDeviceByIdx(int idx)
        {
            try
            {
                cam = null;
                MyCamera.MV_CC_DEVICE_INFO device = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_pDeviceList.pDeviceInfo[idx],
                                                        typeof(MyCamera.MV_CC_DEVICE_INFO));
                if (null == cam)
                {
                    cam = new MyCamera();
                    if (null == cam)
                    {
                        return;
                    }
                }
                int nRet = -1;
                nRet = cam.MV_CC_CreateDevice_NET(ref device);
                if (MyCamera.MV_OK != nRet)
                {
                    cam = null;
                    return;
                }


                nRet = cam.MV_CC_OpenDevice_NET();
                if (MyCamera.MV_OK != nRet)
                {
                    cam.MV_CC_DestroyDevice_NET();
                    logger.Info("Devie open fail");
                    cam = null;
                    return;
                }

                if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    int nPacketSize = cam.MV_CC_GetOptimalPacketSize_NET();
                    if (nPacketSize > 0)
                    {
                        nRet = cam.MV_CC_SetIntValue_NET("GevSCPSPacketSize", (uint)nPacketSize);
                        if (nRet != MyCamera.MV_OK)
                        {
                            //Console.WriteLine("Warning: Set Packet Size failed {0:x8}", nRet);
                            logger.Warn("Warning: Set Packet Size failed {0:x8}");
                        }
                    }
                    else
                    {
                        //Console.WriteLine("Warning: Get Packet Size failed {0:x8}", nPacketSize);
                        logger.Warn("Warning: Get Packet Size failed {0:x8}");
                    }
                }

                cam.MV_CC_SetEnumValue_NET("AcquisitionMode", 2);
                cam.MV_CC_SetEnumValue_NET("TriggerMode", 0);

            }
            catch (Exception ex)
            {
                logger.Error(ex.ToString());
                return;
            }
        }

        public bool Close()
        {
            int nRet;

            nRet = cam.MV_CC_CloseDevice_NET();
            if (MyCamera.MV_OK != nRet)
            {
                return false;
            }

            nRet = cam.MV_CC_DestroyDevice_NET();
            if (MyCamera.MV_OK != nRet)
            {
                return false;
            }
            return true;
        }
        private void GetDeviceBySN(string SN)
        {
            int? idx = null;
            int i = 0;
            foreach (CamInfo info in _ListCamInfo)
            {
                if (info.SN == SN)
                {
                    idx = i;
                    break;
                }
                i++;
            }
            if (idx == null)
                return;

            GetDeviceByIdx((int)idx);
        }




        public static bool ListDevices()
        {
            try
            {
                _ListCamInfo = new ArrayList();
                System.GC.Collect();
                int ret;
                ret = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref m_pDeviceList);
                if (ret != 0)
                {
                    //ShowErrorMsg("Enumerate devices fail!", 0);
                    return false;
                }

                for (int i = 0; i < m_pDeviceList.nDeviceNum; i++)
                {
                    MyCamera.MV_CC_DEVICE_INFO device = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_pDeviceList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                    if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                    {
                        IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stGigEInfo, 0);
                        MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                        CamInfo camInfo = new CamInfo();
                        if (gigeInfo.chUserDefinedName != "")
                        {
                            camInfo.Name = gigeInfo.chUserDefinedName;
                            camInfo.SN = gigeInfo.chSerialNumber;
                        }
                        else
                        {

                            camInfo.Name = gigeInfo.chManufacturerName;
                            camInfo.SN = gigeInfo.chSerialNumber;
                        }
                        _ListCamInfo.Add(camInfo);
                    }
                    else if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
                    {
                        IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stUsb3VInfo, 0);
                        MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                        CamInfo camInfo = new CamInfo();
                        if (usbInfo.chUserDefinedName != "")
                        {

                            camInfo.Name = usbInfo.chUserDefinedName;
                            camInfo.SN = usbInfo.chSerialNumber;
                        }
                        else
                        {
                            camInfo.Name = usbInfo.chManufacturerName;
                            camInfo.SN = usbInfo.chSerialNumber;

                        }
                        _ListCamInfo.Add(camInfo);
                    }
                }
                if (_ListCamInfo.Count > 0)
                    return true;
                return false;
            }
            catch (Exception)
            {
                logger.Warn("Cant load list camera");
                return false;
            }


        }
        private void ShowErrorMsg(string csMessage, int nErrorNum)
        {
            string errorMsg;
            if (nErrorNum == 0)
            {
                errorMsg = csMessage;
            }
            else
            {
                errorMsg = csMessage + ": Error =" + String.Format("{0:X}", nErrorNum);
            }

            switch (nErrorNum)
            {
                case MyCamera.MV_E_HANDLE: errorMsg += " Error or invalid handle "; break;
                case MyCamera.MV_E_SUPPORT: errorMsg += " Not supported function "; break;
                case MyCamera.MV_E_BUFOVER: errorMsg += " Cache is full "; break;
                case MyCamera.MV_E_CALLORDER: errorMsg += " Function calling order error "; break;
                case MyCamera.MV_E_PARAMETER: errorMsg += " Incorrect parameter "; break;
                case MyCamera.MV_E_RESOURCE: errorMsg += " Applying resource failed "; break;
                case MyCamera.MV_E_NODATA: errorMsg += " No data "; break;
                case MyCamera.MV_E_PRECONDITION: errorMsg += " Precondition error, or running environment changed "; break;
                case MyCamera.MV_E_VERSION: errorMsg += " Version mismatches "; break;
                case MyCamera.MV_E_NOENOUGH_BUF: errorMsg += " Insufficient memory "; break;
                case MyCamera.MV_E_UNKNOW: errorMsg += " Unknown error "; break;
                case MyCamera.MV_E_GC_GENERIC: errorMsg += " General error "; break;
                case MyCamera.MV_E_GC_ACCESS: errorMsg += " Node accessing condition error "; break;
                case MyCamera.MV_E_ACCESS_DENIED: errorMsg += " No permission "; break;
                case MyCamera.MV_E_BUSY: errorMsg += " Device is busy, or network disconnected "; break;
                case MyCamera.MV_E_NETER: errorMsg += " Network error "; break;
            }

            System.Windows.Forms.MessageBox.Show(errorMsg, "PROMPT");
        }

        private Boolean IsMonoData(MyCamera.MvGvspPixelType enGvspPixelType)
        {
            switch (enGvspPixelType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12_Packed:
                    return true;

                default:
                    return false;
            }
        }

        private Boolean IsColorData(MyCamera.MvGvspPixelType enGvspPixelType)
        {
            switch (enGvspPixelType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_YUYV_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YCBCR411_8_CBYYCRYY:
                    return true;

                default:
                    return false;
            }
        }

        public Bitmap GetBitmap()
        {
            lock (synLock)
            {

                int nRet;
                UInt32 nPayloadSize = 0;
                MyCamera.MVCC_INTVALUE stParam = new MyCamera.MVCC_INTVALUE();
                nRet = cam.MV_CC_GetIntValue_NET("PayloadSize", ref stParam);
                if (MyCamera.MV_OK != nRet)
                {
                    logger.Error("Get PayloadSize failed");
                    return null;
                }
                nPayloadSize = stParam.nCurValue;
                if (nPayloadSize > m_nBufSizeForDriver)
                {
                    m_nBufSizeForDriver = nPayloadSize;
                    m_pBufForDriver = new byte[m_nBufSizeForDriver];
                    m_nBufSizeForSaveImage = m_nBufSizeForDriver * 3 + 2048;
                    m_pBufForSaveImage = new byte[m_nBufSizeForSaveImage];
                }

                IntPtr pData = Marshal.UnsafeAddrOfPinnedArrayElement(m_pBufForDriver, 0);
                MyCamera.MV_FRAME_OUT_INFO_EX stFrameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();
                nRet = cam.MV_CC_GetOneFrameTimeout_NET(pData, m_nBufSizeForDriver, ref stFrameInfo, 1000);
                if (MyCamera.MV_OK != nRet)
                {
                    logger.Error("No Data!");
                    return null;
                }

                MyCamera.MvGvspPixelType enDstPixelType;
                if (IsMonoData(stFrameInfo.enPixelType))
                {
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8;
                }
                else if (IsColorData(stFrameInfo.enPixelType))
                {
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed;
                }
                else
                {
                    logger.Error("No such pixel type!");
                    return null;
                }

                IntPtr pImage = Marshal.UnsafeAddrOfPinnedArrayElement(m_pBufForSaveImage, 0);
                //MyCamera.MV_SAVE_IMAGE_PARAM_EX stSaveParam = new MyCamera.MV_SAVE_IMAGE_PARAM_EX();
                MyCamera.MV_PIXEL_CONVERT_PARAM stConverPixelParam = new MyCamera.MV_PIXEL_CONVERT_PARAM();
                stConverPixelParam.nWidth = stFrameInfo.nWidth;
                stConverPixelParam.nHeight = stFrameInfo.nHeight;
                stConverPixelParam.pSrcData = pData;
                stConverPixelParam.nSrcDataLen = stFrameInfo.nFrameLen;
                stConverPixelParam.enSrcPixelType = stFrameInfo.enPixelType;
                stConverPixelParam.enDstPixelType = enDstPixelType;
                stConverPixelParam.pDstBuffer = pImage;
                stConverPixelParam.nDstBufferSize = m_nBufSizeForSaveImage;
                nRet = cam.MV_CC_ConvertPixelType_NET(ref stConverPixelParam);
                if (MyCamera.MV_OK != nRet)
                {
                    return null;
                }


                if (enDstPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
                {

                    Bitmap bmp = new Bitmap(stFrameInfo.nWidth, stFrameInfo.nHeight, stFrameInfo.nWidth * 1, System.Drawing.Imaging.PixelFormat.Format8bppIndexed, pImage);

                    ColorPalette cp = bmp.Palette;
                    // init palette
                    for (int i = 0; i < 256; i++)
                    {
                        cp.Entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                    }
                    // set palette back
                    bmp.Palette = cp;

                    return bmp;
                }
                else
                {
                    //*********************RGB8  Bitmap**************************
                    for (int i = 0; i < stFrameInfo.nHeight; i++)
                    {
                        for (int j = 0; j < stFrameInfo.nWidth; j++)
                        {
                            byte chRed = m_pBufForSaveImage[i * stFrameInfo.nWidth * 3 + j * 3];
                            m_pBufForSaveImage[i * stFrameInfo.nWidth * 3 + j * 3] = m_pBufForSaveImage[i * stFrameInfo.nWidth * 3 + j * 3 + 2];
                            m_pBufForSaveImage[i * stFrameInfo.nWidth * 3 + j * 3 + 2] = chRed;
                        }
                    }

                    Bitmap bmp = new Bitmap(stFrameInfo.nWidth, stFrameInfo.nHeight, stFrameInfo.nWidth * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, pImage);
                    return bmp;

                }
            }


        }
    }

    class CamInfo
    {
        public string Name { get; set; }
        public string SN { get; set; }
    }

}
