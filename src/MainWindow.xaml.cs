﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FindDuplicateFiles.Common;
using FindDuplicateFiles.Model;
using FindDuplicateFiles.SearchFile;
using FindDuplicateFiles.ViewModel;
using JiuLing.CommonLibs.ExtensionMethods;

namespace FindDuplicateFiles
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 是否正在进行搜索
        /// </summary>
        private bool _isSearching;

        private readonly SearchFilesJob _searchFilesJob = new();
        private readonly MainWindowViewModel _myModel = new();
        public MainWindow()
        {
            InitializeComponent();

            LoadingAppConfig();
            LoadingTheme(GlobalArgs.AppConfig.Theme);

            BindingItemsSource();
            BindingSearchEvent();

            InitializeSearchCondition();
            InitializeLoadingBlock();
        }

        private void LoadingAppConfig()
        {
            if (!File.Exists(GlobalArgs.AppConfigPath))
            {
                GlobalArgs.AppConfig = new AppConfigInfo();
            }
            else
            {
                string json = File.ReadAllText(GlobalArgs.AppConfigPath);
                GlobalArgs.AppConfig = System.Text.Json.JsonSerializer.Deserialize<AppConfigInfo>(json);
            }
            //TODO 这里可以为配置文件加入版本号，有更新时才覆盖保存
            SaveAppConfig();

            // string updateConfigPath = $"{GlobalArgs.AppPath}{GlobalArgs.UpdateConfigPath}";
            //  string updateConfigString = File.ReadAllText(updateConfigPath);
            // GlobalArgs.UpdateConfig = System.Text.Json.JsonSerializer.Deserialize<UpdateConfigInfo>(updateConfigString);
        }

        private void InitializeSearchCondition()
        {
            //匹配方式
            ChkFileName.IsChecked = true;
            ChkFileSize.IsChecked = true;
            ChkFileLastWriteTimeUtc.IsChecked = true;

            //选项
            ChkIgnoreEmptyFile.IsChecked = true;
            ChkIgnoreHiddenFile.IsChecked = true;
            ChkIgnoreSystemFile.IsChecked = true;
            ChkIgnoreSmallFile.IsChecked = false;
            RdoAllFile.IsChecked = true;

            _myModel.SearchFolders.Clear();
        }


        /// <summary>
        /// UI数据绑定
        /// </summary>
        private void BindingItemsSource()
        {
            ListBoxSearchFolders.ItemsSource = _myModel.SearchFolders;
            ListViewDuplicateFile.ItemsSource = _myModel.DuplicateFiles;

            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(ListViewDuplicateFile.ItemsSource);
            var groupDescription = new PropertyGroupDescription("Key");
            view.GroupDescriptions?.Add(groupDescription);

            RdoOnlyImageFile.ToolTip = $"包括：{GlobalArgs.AppConfig.ImageExtension}文件";
            RdoOnlyMediaFile.ToolTip = $"包括：{GlobalArgs.AppConfig.MediaExtension}文件";
            RdoOnlyDocumentFile.ToolTip = $"包括：{GlobalArgs.AppConfig.DocumentExtension}文件";
            ChkIgnoreSystemFile.ToolTip = $"包括：{GlobalArgs.AppConfig.SystemExtension}文件";
            DataContext = _myModel;
        }

        /// <summary>
        /// 绑定查找任务的内部事件
        /// </summary>
        private void BindingSearchEvent()
        {
            _searchFilesJob.EventMessage = ExecutedMessage;
            _searchFilesJob.EventDuplicateFound = DuplicateFilesFound;
            _searchFilesJob.EventSearchFinished = SearchFinished;
        }
        private void InitializeLoadingBlock()
        {
            var da = new DoubleAnimation()
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(3)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            var rt = new RotateTransform();
            ImgLoading.RenderTransform = rt;
            rt.BeginAnimation(RotateTransform.AngleProperty, da);
        }

        private void ExecutedMessage(string message)
        {
            _myModel.JobMessage = message;
        }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();

            if (GridImage.Visibility == Visibility)
            {
                ImgPreview.Source = null;
                GridImage.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                this.ImgMaximize.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/maximize.png"));
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                this.ImgMaximize.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/restore.png"));
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnAddSearchFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog();
            fbd.ShowDialog();
            var path = fbd.SelectedPath;
            if (path.IsEmpty())
            {
                return;
            }

            if (_myModel.SearchFolders.Contains(path))
            {
                return;
            }
            _myModel.SearchFolders.Add(path);
        }

        private void BtnRemoveSearchFolder_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag;
            if (tag == null)
            {
                return;
            }

            _myModel.SearchFolders.Remove(tag.ToString());
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                if (MessageBox.Show("确定要停止搜索吗？", "重复文件查找", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
                {
                    return;
                }
                EndSearch();
            }
            else
            {
                BeginSearch();
            }
        }

        private void BeginSearch()
        {
            if (_myModel.SearchFolders.Count == 0)
            {
                MessageBox.Show("请选择要查找的文件夹", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            //匹配方式校验
            SearchMatchEnum searchMatch = 0;
            if (ChkFileName.IsChecked == true)
            {
                searchMatch |= SearchMatchEnum.Name;
            }
            if (ChkFileSize.IsChecked == true)
            {
                searchMatch |= SearchMatchEnum.Size;
            }
            if (ChkFileLastWriteTimeUtc.IsChecked == true)
            {
                searchMatch |= SearchMatchEnum.LastWriteTime;
            }
            if (searchMatch == 0)
            {
                MessageBox.Show("请选择匹配方式", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            //选项
            SearchOptionEnum searchOption = 0;
            if (ChkIgnoreEmptyFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.IgnoreEmptyFile;
            }
            if (ChkIgnoreHiddenFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.IgnoreHiddenFile;
            }
            if (ChkIgnoreSystemFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.IgnoreSystemFile;
            }
            if (ChkIgnoreSmallFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.IgnoreSmallFile;
            }
            if (RdoOnlyImageFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.OnlyImageFile;
            }
            if (RdoOnlyMediaFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.OnlyMediaFile;
            }
            if (RdoOnlyDocumentFile.IsChecked == true)
            {
                searchOption |= SearchOptionEnum.OnlyDocumentFile;
            }

            SetBeginSearchStyle();
            _myModel.DuplicateFiles.Clear();
            var config = new SearchConfigs()
            {
                Folders = new List<string>(_myModel.SearchFolders.ToList()),
                SearchMatch = searchMatch,
                SearchOption = searchOption
            };
            _searchFilesJob.Start(config);
        }

        /// <summary>
        /// 开始搜索
        /// </summary>
        private void SetBeginSearchStyle()
        {
            TxtSearch.Text = "停止";
            ImgSearch.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/stop.png"));
            _isSearching = true;
            _myModel.IsShowLoading = _isSearching;
        }

        private void EndSearch()
        {
            SetEndSearchStyle();
            _searchFilesJob.Stop();
        }

        /// <summary>
        /// 结束搜索
        /// </summary>
        private void SetEndSearchStyle()
        {
            TxtSearch.Text = "查找";
            ImgSearch.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/search.png"));
            _isSearching = false;
            _myModel.IsShowLoading = _isSearching;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                MessageBox.Show("任务执行中，禁止重置", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }
            InitializeSearchCondition();
        }

        private void ChangeTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                MessageBox.Show("任务执行中不允许换肤", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            var tag = (sender as Button)?.Tag;
            if (tag == null)
            {
                MessageBox.Show("修改主题失败：系统错误", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string theme = tag.ToString();
            GlobalArgs.AppConfig.Theme = theme;

            SaveAppConfig();

            LoadingTheme(theme);
        }

        private void LoadingTheme(string themeName)
        {
            try
            {
                var mResourceSkin = new ResourceDictionary()
                {
                    Source = new Uri($"/Themes/Theme{themeName}.xaml", UriKind.RelativeOrAbsolute)
                };
                Application.Current.Resources.MergedDictionaries[0] = mResourceSkin;

                ImgSearch.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/search.png"));
                ImgReset.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/reset.png"));
                ImgMultipleChoice.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/multiple_choice.png"));
                ImgDeleteBin.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/delete_bin.png"));
                ImgLoading.Source = new BitmapImage(new Uri($"pack://application:,,,/Images/Themes/{GlobalArgs.AppConfig.Theme}/loader.png"));
            }
            catch (IOException ex)
            {
                MessageBox.Show($"主题加载失败，{ex.Message}", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DuplicateFilesFound(string key, SimpleFileInfo simpleFile)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _myModel.DuplicateFiles.Add(new DuplicateFileModel()
                {
                    Key = key,
                    Name = simpleFile.Name,
                    Path = simpleFile.Path,
                    Size = Math.Ceiling(simpleFile.Size / 1024),
                    LastWriteTime = simpleFile.LastWriteTime,
                    Extension = simpleFile.Extension,
                    IsCheckedFile = false
                });
            });
        }
        private void SearchFinished()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SetEndSearchStyle();
                MessageBox.Show("查找完成", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void ListViewDuplicateFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var lv = sender as ListView;
            if (lv?.SelectedItem is not DuplicateFileModel selectFile)
            {
                return;
            }
            if (selectFile.Extension.IsNotEmpty() && GlobalArgs.AppConfig.ImageExtension.Contains(selectFile.Extension))
            {
                ImgPreview.Source = new BitmapImage(new Uri(selectFile.Path, UriKind.Absolute));
                GridImage.Visibility = Visibility.Visible;
            }
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }
        private void BtnChooseFile_Click(object sender, RoutedEventArgs e)
        {
            if (_myModel.DuplicateFiles.Count == 0)
            {
                MessageBox.Show("没有可用数据", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_myModel.DuplicateFiles.Any(x => x.IsCheckedFile))
            {
                RemoveChooseAllFiles();
            }
            else
            {
                ChooseAllFiles();
            }

        }

        /// <summary>
        /// 是否在全选操作
        /// </summary>
        private bool _isSelectingAll = false;
        private void ChooseAllFiles()
        {
            _isSelectingAll = true;
            foreach (var duplicateFile in _myModel.DuplicateFiles)
            {
                duplicateFile.IsCheckedFile = true;
            }
            var keyList = _myModel.DuplicateFiles.GroupBy(x => x.Key);
            foreach (var keyItem in keyList)
            {
                _myModel.DuplicateFiles.First(x => x.Key == keyItem.Key).IsCheckedFile = false;
            }
            _isSelectingAll = false;
        }


        private void RemoveChooseAllFiles()
        {
            foreach (var duplicateFile in _myModel.DuplicateFiles)
            {
                duplicateFile.IsCheckedFile = false;
            }
        }
        private void BtnDeleteFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_myModel.DuplicateFiles.Count == 0)
                {
                    MessageBox.Show("没有可用数据", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (MessageBox.Show($"确认要删除选中文件吗？文件删除后不可恢复！", "重复文件查找", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                foreach (var file in _myModel.DuplicateFiles)
                {
                    if (file.IsCheckedFile == false)
                    {
                        continue;
                    }
                    System.IO.File.Delete(file.Path);
                }

                _myModel.DuplicateFiles.Clear();
                MessageBox.Show("删除完成", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败：{ex.Message}", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChkIsChecked_Checked(object sender, RoutedEventArgs e)
        {
            if (_isSelectingAll)
            {
                //全选操作时，不处理事件
                return;
            }

            var chk = (sender as CheckBox);
            if (chk == null)
            {
                MessageBox.Show("选择失败，系统内部错误", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (chk.DataContext is not DuplicateFileModel checkedFile)
            {
                MessageBox.Show("选择失败，系统内部错误", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_myModel.DuplicateFiles.Any(x => x.Key == checkedFile.Key && x.IsCheckedFile == false))
            {
                MessageBox.Show("必须至少保留重复文件中的一个", "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Stop);
                chk.IsChecked = false;
                return;
            }
        }

        private void SaveAppConfig()
        {
            var directory = Path.GetDirectoryName(GlobalArgs.AppConfigPath) ?? throw new ArgumentException("配置文件路径异常");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string appConfigString = System.Text.Json.JsonSerializer.Serialize(GlobalArgs.AppConfig);
            File.WriteAllText(GlobalArgs.AppConfigPath, appConfigString);
        }
    }
}
