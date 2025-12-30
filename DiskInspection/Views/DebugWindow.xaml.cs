using DiskInspection.Models;
using DiskInspection.Utils;
using DiskInspection.Views.UtilitiesWindows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Windows.Shapes;

namespace DiskInspection.Views
{
    /// <summary>
    /// Interaction logic for DebugWindow.xaml
    /// </summary>
    public partial class DebugWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableCollection<ImageDebugInfo> ImagesInfoList { get; set; } = new ObservableCollection<ImageDebugInfo>();
        private ImageDebugInfo _selectedImageInfo;
        public ImageDebugInfo SelectedImageInfo
        {
            get => _selectedImageInfo;
            set
            {
                if (_selectedImageInfo != value)
                {
                    _selectedImageInfo = value;
                    OnPropertyChanged();
                    UpdateSetlectionChanged();
                }
            }
        }

        private void UpdateSetlectionChanged()
        {
          
        }

        public DebugWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void btnLoadFolder_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void btnLoadImages_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                var fileName = openFileDialog.FileNames;
                if (fileName.Length == 0)
                {
                    var error = new ErrorWindow("Image paths is empty!\rĐường dẫn ảnh rỗng!");
                    error.ShowDialog();
                    return;
                }
                foreach (var path in fileName)
                {
                    if (ImagesInfoList.Select(obj => obj.FilePath).ToList().Contains(path))
                    {
                        var error = new ErrorWindow($"{IO.GetFileName(path)} is already existed in list!\r{IO.GetFileName(path)} ảnh đã tồn tại trong danh sách!");
                        error.ShowDialog();
                    }
                    else
                    {
                        var newImageInfo = new ImageDebugInfo(ImagesInfoList.Count + 1, path);
                        ImagesInfoList.Add(newImageInfo);
                    }
                }
                SelectedImageInfo = ImagesInfoList[ImagesInfoList.Count - 1];
                dgImageInfoPaths.ScrollIntoView(SelectedImageInfo);
            }
        }
    }
}
