using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Net.Mime.MediaTypeNames;

namespace DiskInspection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var testImg1 = new Bitmap(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\raw_data\25_12\Image__2025-12-25__23-52-55.bmp");  
            var testImg2 = new Bitmap(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\73efebb9-933d-4ff9-960f-7e9419693213.jfif");
            var scale1 = GetFittedZoomScale(imbCam1, testImg1.Width, testImg1.Height);
            var scale2 = GetFittedZoomScale(imbCam1Result, testImg2.Width, testImg2.Height);
            var scale3 = GetFittedZoomScale(imbCam1UV, testImg2.Width, testImg2.Height);
            imbCam1.SourceFromBitmap = testImg1;
            imbCam1Result.SourceFromBitmap = testImg2;
            imbCam1.SetZoomScale(scale1);
            imbCam1Result.SetZoomScale(scale2);
            imbCam1UV.SourceFromBitmap = testImg2;
            imbCam1Result.SetZoomScale(scale3);
        }
        private double GetFittedZoomScale(object imb, double imageWidth, double imageHeight)
        {
            var imageBox = imb as Heal.MyControl.ImageBox;
            var imageBoxWidth = imageBox.ActualWidth;
            var imageBoxHeight = imageBox.ActualHeight;
            var scale = Math.Min(imageBoxWidth / imageWidth, imageBoxHeight / imageHeight);
            return scale;
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
