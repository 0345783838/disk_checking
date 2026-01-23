using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DiskInspection.Views
{
    /// <summary>
    /// Interaction logic for PopUpErrorWindow.xaml
    /// </summary>
    public partial class ImageViewerWindow : Window
    {
        private Point _start;
        private Point _origin;
        private List<BitmapImage> _imageList = new List<BitmapImage>();
        private int _currentIndex = -1;
        private const double EDGE_ZONE = 150;
        public ImageViewerWindow()
        {
            InitializeComponent();
            var bm1 = new BitmapImage(new Uri(@"C:\Users\Admin\Downloads\591e0a63-ca0b-4202-9a02-1e7cbf8c14ca.jfif"));
            var bm2 = new BitmapImage(new Uri(@"C:\Users\Admin\Downloads\95a90a7b-2723-4ff3-9514-b5f7d0dbd09b.jfif"));
            var bm3 = new BitmapImage(new Uri(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\raw_data\07_12\Image__2025-12-07__23-51-12.bmp"));
            var bm4 = new BitmapImage(new Uri(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\raw_data\07_12\Image__2025-12-07__23-56-47.bmp"));
            var bm5 = new BitmapImage(new Uri(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\raw_data\uv\Image__2025-12-04__21-43-09.bmp"));
               
            var list = new List<BitmapImage> { bm1, bm2, bm3, bm4, bm5 };
            LoadImages(list);

        }
        public void LoadImages( List<BitmapImage> imageList, int errorIdx=0)
        {
            _imageList.Clear();
            _imageList = imageList;
            lbThumbList.ItemsSource = _imageList;

            if (_imageList.Count > 0 && errorIdx>0 && errorIdx < _imageList.Count)
            {
                _currentIndex = errorIdx;
                LoadMainImage();
            }
        }

        private void LoadMainImage()
        {
            if (_currentIndex < 0 || _currentIndex >= _imageList.Count)
                return;

            imbImage.Source = _imageList[_currentIndex];
            ResetView();

            lbThumbList.SelectedIndex = _currentIndex;
            lbThumbList.ScrollIntoView(lbThumbList.SelectedItem);
        }

        private void imbImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            imbImage.CaptureMouse();
            _start = e.GetPosition(this);
            _origin = new Point(translateTransform.X, translateTransform.Y);
            Cursor = Cursors.Hand;
        }

        private void imbImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            imbImage.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }

        private void imbImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!imbImage.IsMouseCaptured) return;

            Point p = e.GetPosition(this);
            translateTransform.X = _origin.X + (p.X - _start.X);
            translateTransform.Y = _origin.Y + (p.Y - _start.Y);
        }

        private void imbImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoom = e.Delta > 0 ? 1.1 : 0.9;

            scaleTransform.ScaleX *= zoom;
            scaleTransform.ScaleY *= zoom;
        }

        private void imbImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                ResetView();
        }
        private async  void ResetView()
        {
            var duration = TimeSpan.FromMilliseconds(150);

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, duration));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, duration));

            translateTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, duration));
            translateTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, duration));

            await Task.Delay(duration);

            // Xóa animation, trả quyền điều khiển về code
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);

            // Set giá trị thực
            scaleTransform.ScaleX = 1;
            scaleTransform.ScaleY = 1;
            translateTransform.X = 0;
            translateTransform.Y = 0;
        }

        private void lbThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lbThumbList.SelectedIndex < 0) return;

            _currentIndex = lbThumbList.SelectedIndex;
            LoadMainImage();
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex <= 0) return;

            _currentIndex--;
            LoadMainImage();
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex >= _imageList.Count - 1) return;

            _currentIndex++;
            LoadMainImage();
        }

        private void ShowButton(Button btn)
        {
            if (btn.Visibility == Visibility.Visible && btn.Opacity == 1)
                return;

            btn.Visibility = Visibility.Visible;
            btn.IsHitTestVisible = true;

            var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            btn.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        private void HideButton(Button btn)
        {
            if (btn.Visibility != Visibility.Visible)
                return;

            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (s, e) =>
            {
                btn.Visibility = Visibility.Collapsed;
                btn.IsHitTestVisible = false;
            };

            btn.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void grView_MouseMove(object sender, MouseEventArgs e)
        {
            if (_imageList.Count <= 1)
            {
                HideButton(btnBack);
                HideButton(btnNext);
                return;
            }

            Point p = e.GetPosition(bdView);
            double w = bdView.ActualWidth;

            bool nearLeft = p.X <= EDGE_ZONE;
            bool nearRight = p.X >= w - EDGE_ZONE;

            if (nearLeft && _currentIndex > 0)
                ShowButton(btnBack);
            else
                HideButton(btnBack);

            if (nearRight && _currentIndex < _imageList.Count - 1)
                ShowButton(btnNext);
            else
                HideButton(btnNext);
        }

        private void grView_MouseLeave(object sender, MouseEventArgs e)
        {
            HideButton(btnBack);
            HideButton(btnNext);
        }
    }
}
