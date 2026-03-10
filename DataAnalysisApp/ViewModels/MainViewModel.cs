using DataAnalysisApp.Models;
using DataAnalysisApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace DataAnalysisApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly CsvService _csvService;
        private string _selectedFolder;
        private string _searchKeyword;
        private string _statusMessage;
        private int _totalCsvFiles;
        private ObservableCollection<CsvFileInfo> _csvFiles;
        private ObservableCollection<SearchResultGroup> _searchResultGroups;
        private SearchResultGroup _selectedGroup;
        private SearchResult _selectedResult;
        private List<string> _selectedFolders;
        private bool _enableDateFilter;
        private bool _isLoading;
        private DateTime? _startDate;
        private DateTime? _endDate;
        private int _pageSize = 50;
        private int _currentPage = 1;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            _csvService = new CsvService();
            _csvFiles = new ObservableCollection<CsvFileInfo>();
            _searchResultGroups = new ObservableCollection<SearchResultGroup>();
            _searchKeyword = string.Empty;
            _selectedFolders = new List<string>();
            _enableDateFilter = false;
            _startDate = null;
            _endDate = null;

            if (Directory.Exists(@"D:\CSV参数保存"))
            {
                _selectedFolders.Add(@"D:\CSV参数保存");
                _selectedFolder = @"D:\CSV参数保存";
                // 延迟加载，避免构造函数中的异步调用
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await LoadCsvFiles();
                    await Search();
                });
            }

            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            SearchCommand = new RelayCommand(async () => await Search());
            OpenFileLocationCommand = new RelayCommand(OpenFileLocation);
            OpenFileCommand = new RelayCommand(OpenFile);
            ExportFileCommand = new RelayCommand(ExportFile);

            // 初始化分页命令
            FirstPageCommand = new RelayCommand(() => CurrentPage = 1);
            PreviousPageCommand = new RelayCommand(() => CurrentPage--, () => CurrentPage > 1);
            NextPageCommand = new RelayCommand(() => CurrentPage++, () => CurrentPage < TotalPages);
            LastPageCommand = new RelayCommand(() => CurrentPage = TotalPages);
        }

        public string SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                _selectedFolder = value;
                OnPropertyChanged();
            }
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                _searchKeyword = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public int TotalCsvFiles
        {
            get => _totalCsvFiles;
            set
            {
                _totalCsvFiles = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<CsvFileInfo> CsvFiles
        {
            get => _csvFiles;
            set
            {
                _csvFiles = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<SearchResultGroup> SearchResultGroups
        {
            get => _searchResultGroups;
            set
            {
                _searchResultGroups = value;
                OnPropertyChanged();
            }
        }

        public SearchResultGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                _selectedGroup = value;
                OnPropertyChanged();

                if (_selectedGroup != null)
                {
                    // 加载表组的合并数据
                    LoadGroupCombinedData();
                }
                else
                {
                    SelectedResult = null;
                }
            }
        }

        public SearchResult SelectedResult
        {
            get => _selectedResult;
            set
            {
                _selectedResult = value;
                _currentPage = 1; // 重置到第一页

                // 延迟加载：如果选择的结果没有加载数据，则加载数据
                if (_selectedResult != null && _selectedResult.FilteredData == null)
                {
                    LoadSelectedResultData();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PaginatedData));
            }
        }

        private void LoadSelectedResultData()
        {
            if (_selectedResult == null)
                return;

            try
            {
                // 在后台线程加载数据
                System.Threading.Tasks.Task.Run(() =>
                {
                    var csvInfo = _csvService.LoadCsvFile(_selectedResult.FilePath);
                    if (csvInfo != null && csvInfo.Data != null)
                    {
                        // 在UI线程更新数据
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _selectedResult.FilteredData = csvInfo.Data;
                            // 确保触发所有相关属性的更新
                            OnPropertyChanged(nameof(SelectedResult));
                            OnPropertyChanged(nameof(PaginatedData));
                            OnPropertyChanged(nameof(TotalPages));
                            OnPropertyChanged(nameof(CurrentPage));
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载文件数据失败: {ex.Message}");
            }
        }

        private void LoadGroupCombinedData()
        {
            if (_selectedGroup == null || _selectedGroup.Results == null || _selectedGroup.Results.Count == 0)
                return;

            try
            {
                // 在后台线程加载数据
                System.Threading.Tasks.Task.Run(() =>
                {
                    var combinedTable = new DataTable();
                    bool isFirstFile = true;
                    int totalRows = 0;
                    string keyword = _searchKeyword?.ToLower() ?? string.Empty;
                    bool hasKeyword = !string.IsNullOrWhiteSpace(keyword);

                    foreach (var result in _selectedGroup.Results)
                    {
                        var csvInfo = _csvService.LoadCsvFile(result.FilePath);
                        if (csvInfo == null || csvInfo.Data == null)
                            continue;

                        if (isFirstFile)
                        {
                            // 第一个文件，创建表结构
                            foreach (DataColumn column in csvInfo.Data.Columns)
                            {
                                combinedTable.Columns.Add(column.ColumnName, typeof(string));
                            }
                            isFirstFile = false;
                        }

                        // 添加数据行
                        foreach (DataRow row in csvInfo.Data.Rows)
                        {
                            // 如果有搜索关键词，则只添加包含关键词的行
                            if (hasKeyword)
                            {
                                bool found = false;
                                foreach (DataColumn column in csvInfo.Data.Columns)
                                {
                                    var cellValue = row[column]?.ToString()?.ToLower();
                                    if (cellValue != null && cellValue.Contains(keyword))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                    continue;
                            }

                            var newRow = combinedTable.NewRow();
                            for (int i = 0; i < Math.Min(csvInfo.Data.Columns.Count, combinedTable.Columns.Count); i++)
                            {
                                newRow[i] = row[i];
                            }
                            combinedTable.Rows.Add(newRow);
                            totalRows++;
                        }
                    }

                    // 在UI线程更新数据
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _selectedGroup.CombinedData = combinedTable;

                        // 创建一个特殊的SearchResult来显示合并数据
                        var combinedResult = new SearchResult
                        {
                            FileName = $"{_selectedGroup.GroupName} (总表 - {_selectedGroup.Results.Count}个文件)",
                            FilePath = "",
                            FolderPath = "",
                            MatchedHeaders = _selectedGroup.Headers,
                            FilteredData = combinedTable
                        };

                        SelectedResult = combinedResult;
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载表组合并数据失败: {ex.Message}");
            }
        }

        public bool EnableDateFilter
        {
            get => _enableDateFilter;
            set
            {
                _enableDateFilter = value;
                OnPropertyChanged();
            }
        }

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                _startDate = value;
                OnPropertyChanged();
            }
        }

        public DateTime? EndDate
        {
            get => _endDate;
            set
            {
                _endDate = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                _pageSize = value;
                _currentPage = 1; // 重置到第一页
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PaginatedData));
            }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                _currentPage = Math.Max(1, Math.Min(value, TotalPages));
                OnPropertyChanged();
                OnPropertyChanged(nameof(PaginatedData));
            }
        }

        public int TotalPages
        {
            get
            {
                if (SelectedResult?.FilteredData == null)
                    return 1;
                return (int)Math.Ceiling((double)SelectedResult.FilteredData.Rows.Count / _pageSize);
            }
        }

        public System.Data.DataView PaginatedData
        {
            get
            {
                if (SelectedResult?.FilteredData == null)
                {
                    // 返回空的DataView以避免绑定问题
                    return new System.Data.DataTable().DefaultView;
                }

                int startIndex = (_currentPage - 1) * _pageSize;
                int endIndex = Math.Min(startIndex + _pageSize, SelectedResult.FilteredData.Rows.Count);

                var table = SelectedResult.FilteredData.Clone();
                for (int i = startIndex; i < endIndex; i++)
                {
                    table.ImportRow(SelectedResult.FilteredData.Rows[i]);
                }

                return table.DefaultView;
            }
        }

        public ICommand FirstPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }

        public ICommand BrowseFolderCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand OpenFileLocationCommand { get; }
        public ICommand ExportFileCommand { get; }
        public ICommand OpenFileCommand { get; }

        private async void BrowseFolder()
        {
            // 使用FolderBrowserDialog无法实现多选，所以使用OpenFileDialog作为替代方案
            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "选择包含CSV文件的文件夹",
                FileName = "选择文件夹",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                Multiselect = true,
                Filter = "文件夹|*.*"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedFolders.Clear();
                foreach (var fileName in dialog.FileNames)
                {
                    string selectedFolder = Path.GetDirectoryName(fileName);
                    if (!_selectedFolders.Contains(selectedFolder))
                    {
                        _selectedFolders.Add(selectedFolder);
                    }
                }
                SelectedFolder = string.Join("; ", _selectedFolders);
                await LoadCsvFiles();
                await Search();
            }
        }

        private async System.Threading.Tasks.Task LoadCsvFiles()
        {
            if (_selectedFolders == null || _selectedFolders.Count == 0)
                return;

            try
            {
                StatusMessage = "正在加载CSV文件...";
                IsLoading = true;

                // 将文件加载操作移到后台线程
                var resultFiles = await System.Threading.Tasks.Task.Run(() =>
                {
                    var allFiles = new System.Collections.Concurrent.ConcurrentBag<CsvFileInfo>();

                    // 并行处理多个文件夹
                    System.Threading.Tasks.Parallel.ForEach(_selectedFolders, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount * 2 }, folder =>
                    {
                        if (Directory.Exists(folder))
                        {
                            var files = _csvService.FindAllCsvFiles(folder);

                            // 应用日期筛选
                            if (_enableDateFilter)
                            {
                                files = files.Where(f =>
                                {
                                    // 使用文件修改日期
                                    var fileDate = File.GetLastWriteTime(f.FilePath).Date;

                                    bool afterStart = !_startDate.HasValue || fileDate >= _startDate.Value.Date;
                                    bool beforeEnd = !_endDate.HasValue || fileDate <= _endDate.Value.Date;
                                    return afterStart && beforeEnd;
                                }).ToList();
                            }

                            foreach (var file in files)
                            {
                                allFiles.Add(file);
                            }
                        }
                    });

                    return allFiles.ToList();
                });

                // 在UI线程更新数据
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CsvFiles.Clear();
                    foreach (var file in resultFiles)
                    {
                        CsvFiles.Add(file);
                    }
                });

                TotalCsvFiles = resultFiles.Count;
                StatusMessage = $"已加载 {resultFiles.Count} 个CSV文件";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                MessageBox.Show($"加载CSV文件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async System.Threading.Tasks.Task Search()
        {
            if (CsvFiles == null || CsvFiles.Count == 0)
                return;

            try
            {
                StatusMessage = "正在搜索...";
                IsLoading = true;
                var keyword = SearchKeyword ?? string.Empty;

                // 应用日期过滤
                var filteredFiles = CsvFiles.ToList();
                if (_enableDateFilter)
                {
                    filteredFiles = filteredFiles.Where(f =>
                    {
                        var fileDate = File.GetLastWriteTime(f.FilePath).Date;
                        bool afterStart = !_startDate.HasValue || fileDate >= _startDate.Value.Date;
                        bool beforeEnd = !_endDate.HasValue || fileDate <= _endDate.Value.Date;
                        return afterStart && beforeEnd;
                    }).ToList();
                }

                // 将搜索操作移到后台线程
                var searchResults = await System.Threading.Tasks.Task.Run(() =>
                {
                    var results = _csvService.SearchByContent(filteredFiles, keyword);
                    var groups = _csvService.GroupResultsByHeaderSignature(results);
                    return new { Results = results, Groups = groups };
                });

                // 在UI线程更新数据
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SearchResultGroups.Clear();
                    int groupIndex = 1;
                    foreach (var group in searchResults.Groups)
                    {
                        var resultGroup = new SearchResultGroup
                        {
                            GroupName = $"表组 {groupIndex}",
                            HeaderSignature = group.Key,
                            Headers = group.Value.First().MatchedHeaders,
                            Results = new ObservableCollection<SearchResult>(group.Value)
                        };
                        SearchResultGroups.Add(resultGroup);
                        groupIndex++;
                    }

                    if (SearchResultGroups.Count > 0)
                    {
                        SelectedGroup = SearchResultGroups.First();
                    }
                    else
                    {
                        SelectedGroup = null;
                        SelectedResult = null;
                    }
                });

                StatusMessage = $"搜索完成，显示 {searchResults.Results.Count} 个表，分为 {searchResults.Groups.Count} 个表组";
            }
            catch (Exception ex)
            {
                StatusMessage = $"搜索失败: {ex.Message}";
                MessageBox.Show($"搜索时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void Refresh()
        {
            if (!string.IsNullOrWhiteSpace(SelectedFolder))
            {
                await LoadCsvFiles();
                await Search();
            }
        }

        private void OpenFileLocation()
        {
            if (SelectedResult != null && !string.IsNullOrWhiteSpace(SelectedResult.FilePath))
            {
                try
                {
                    string folderPath = Path.GetDirectoryName(SelectedResult.FilePath);
                    if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folderPath);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"打开文件位置失败: {ex.Message}";
                    MessageBox.Show($"打开文件位置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private DateTime? ExtractDateFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            // 尝试匹配常见的日期格式
            // 格式1: YYYY-MM-DD 或 YYYY-MM-DD
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})-(\d{1,2})-(\d{1,2})");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int year) &&
                    int.TryParse(match.Groups[2].Value, out int month) &&
                    int.TryParse(match.Groups[3].Value, out int day))
                {
                    try
                    {
                        return new DateTime(year, month, day);
                    }
                    catch
                    {
                        // 日期无效，继续尝试其他格式
                    }
                }
            }

            // 格式2: YYYYMMDD
            match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})(\d{2})(\d{2})");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int year) &&
                    int.TryParse(match.Groups[2].Value, out int month) &&
                    int.TryParse(match.Groups[3].Value, out int day))
                {
                    try
                    {
                        return new DateTime(year, month, day);
                    }
                    catch
                    {
                        // 日期无效，继续尝试其他格式
                    }
                }
            }

            // 格式3: YYYY/MM/DD
            match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})/(\d{1,2})/(\d{1,2})");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int year) &&
                    int.TryParse(match.Groups[2].Value, out int month) &&
                    int.TryParse(match.Groups[3].Value, out int day))
                {
                    try
                    {
                        return new DateTime(year, month, day);
                    }
                    catch
                    {
                        // 日期无效，继续尝试其他格式
                    }
                }
            }

            return null;
        }

        private void OpenFile()
        {
            if (SelectedResult != null && !string.IsNullOrWhiteSpace(SelectedResult.FilePath))
            {
                try
                {
                    if (File.Exists(SelectedResult.FilePath))
                    {
                        System.Diagnostics.Process.Start(SelectedResult.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"打开文件失败: {ex.Message}";
                    MessageBox.Show($"打开文件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportFile()
        {
            if (SelectedResult != null)
            {
                try
                {
                    var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "CSV文件 (*.csv)|*.csv",
                        FileName = $"导出_{System.IO.Path.GetFileNameWithoutExtension(SelectedResult.FilePath)}.csv"
                    };

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        var dataTable = SelectedResult.FilteredData;
                        using (var writer = new System.IO.StreamWriter(saveFileDialog.FileName, false, System.Text.Encoding.UTF8))
                        {
                            // 写入表头
                            var headers = dataTable.Columns.Cast<System.Data.DataColumn>().Select(col => col.ColumnName);
                            writer.WriteLine(string.Join(",", headers));

                            // 写入数据行
                            foreach (System.Data.DataRow row in dataTable.Rows)
                            {
                                var values = row.ItemArray.Select(item =>
                                {
                                    var value = item?.ToString() ?? "";
                                    // 如果值包含逗号、引号或换行符，需要用引号包围
                                    if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                                    {
                                        value = $"\"{value.Replace("\"", "\"\"")}\"";
                                    }
                                    return value;
                                });
                                writer.WriteLine(string.Join(",", values));
                            }
                        }

                        System.Windows.MessageBox.Show("导出成功！");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"导出文件失败: {ex.Message}";
                    System.Windows.MessageBox.Show($"导出文件时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SearchResultGroup
    {
        public string GroupName { get; set; }
        public string HeaderSignature { get; set; }
        public List<string> Headers { get; set; }
        public ObservableCollection<SearchResult> Results { get; set; }
        public DataTable CombinedData { get; set; }

        public string HeadersDisplay => Headers != null ? string.Join(", ", Headers) : string.Empty;
        public int ResultCount => Results?.Count ?? 0;
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }
}