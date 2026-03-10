using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using DataAnalysisApp.Models;
using DataAnalysisApp.ViewModels;

namespace DataAnalysisApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                var element = e.OriginalSource as FrameworkElement;
                if (element != null)
                {
                    // 检查是否点击的是展开/折叠按钮
                    var parent = VisualTreeHelper.GetParent(element);
                    while (parent != null)
                    {
                        var toggleButton = parent as System.Windows.Controls.Primitives.ToggleButton;
                        if (toggleButton != null)
                        {
                            // 点击的是展开/折叠按钮，不执行选择操作
                            return;
                        }
                        parent = VisualTreeHelper.GetParent(parent);
                    }

                    var dataContext = GetDataContext(element);
                    if (dataContext is SearchResult searchResult)
                    {
                        viewModel.SelectedResult = searchResult;
                    }
                    else if (dataContext is SearchResultGroup searchResultGroup)
                    {
                        viewModel.SelectedGroup = searchResultGroup;
                    }
                }
            }
        }

        private void TreeView_Loaded(object sender, RoutedEventArgs e)
        {
            // 使TreeView初始为折叠状态
            var treeView = sender as TreeView;
            if (treeView != null)
            {
                foreach (var item in treeView.Items)
                {
                    var treeViewItem = treeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                    if (treeViewItem != null)
                    {
                        treeViewItem.IsExpanded = false;
                    }
                }
            }
        }

        private object GetDataContext(FrameworkElement element)
        {
            while (element != null && element.DataContext == null)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }
            return element?.DataContext;
        }

        private void ToggleButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (LeftPanel.Width == 242)
            {
                // 收缩面板
                var animation = new System.Windows.Media.Animation.DoubleAnimation(242, 0, new Duration(TimeSpan.FromSeconds(0.3)));
                LeftPanel.BeginAnimation(Border.WidthProperty, animation);
                (sender as Border).Child = new TextBlock { Text = ">", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0x66, 0x66, 0x66)), FontWeight = FontWeights.Bold };
            }
            else
            {
                // 展开面板
                var animation = new System.Windows.Media.Animation.DoubleAnimation(0, 242, new Duration(TimeSpan.FromSeconds(0.3)));
                LeftPanel.BeginAnimation(Border.WidthProperty, animation);
                (sender as Border).Child = new TextBlock { Text = "<", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0x66, 0x66, 0x66)), FontWeight = FontWeights.Bold };
            }
        }

        private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    await viewModel.Search();
                }
            }
        }

    }
}