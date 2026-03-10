using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataAnalysisApp.Models;

namespace DataAnalysisApp.Services
{
    public class CsvService
    {
        public List<CsvFileInfo> FindAllCsvFiles(string rootFolder)
        {
            var csvFiles = new ConcurrentBag<CsvFileInfo>();

            if (!Directory.Exists(rootFolder))
                return csvFiles.ToList();

            try
            {
                var files = Directory.GetFiles(rootFolder, "*.csv", SearchOption.AllDirectories);

                // 使用并行处理加载文件元数据（不加载实际数据）
                Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, file =>
                {
                    try
                    {
                        var csvInfo = LoadCsvFileMetadata(file);
                        if (csvInfo != null)
                        {
                            csvFiles.Add(csvInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"加载CSV文件元数据失败: {file}, 错误: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"查找CSV文件失败: {ex.Message}");
            }

            return csvFiles.ToList();
        }

        public CsvFileInfo LoadCsvFileMetadata(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var csvInfo = new CsvFileInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FolderPath = Path.GetDirectoryName(filePath),
                IsDataLoaded = false
            };

            // 只读取表头，不读取数据
            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string headerLine = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(headerLine))
                    {
                        csvInfo.Headers = ParseCsvLine(headerLine);
                        return csvInfo;
                    }
                }

                // 如果UTF-8失败，尝试其他编码
                var encodings = new[] { Encoding.Default, Encoding.GetEncoding(936), Encoding.GetEncoding(1252) };
                foreach (var encoding in encodings)
                {
                    try
                    {
                        using (var reader = new StreamReader(filePath, encoding))
                        {
                            string headerLine = reader.ReadLine();
                            if (!string.IsNullOrWhiteSpace(headerLine))
                            {
                                csvInfo.Headers = ParseCsvLine(headerLine);
                                return csvInfo;
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        public void LoadCsvFileData(CsvFileInfo csvInfo)
        {
            if (csvInfo == null || csvInfo.IsDataLoaded || !File.Exists(csvInfo.FilePath))
                return;

            var dataTable = new DataTable();
            string[] lines = null;
            Encoding selectedEncoding = null;

            // 快速检测文件大小，如果文件太大，使用更高效的读取方式
            var fileInfo = new FileInfo(csvInfo.FilePath);
            if (fileInfo.Length > 10 * 1024 * 1024) // 10MB以上的文件
            {
                // 对于大文件，使用流式读取
                LoadLargeCsvFileData(csvInfo, dataTable);
                return;
            }

            // 对于小文件，使用内存读取
            byte[] bytes = null;
            try
            {
                bytes = File.ReadAllBytes(csvInfo.FilePath);
            }
            catch
            {
                return;
            }

            // 首先检测文件是否有BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                // UTF-8 with BOM
                try
                {
                    string content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                    lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    selectedEncoding = Encoding.UTF8;
                }
                catch
                {
                    // 解析失败，继续尝试其他编码
                }
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                // UTF-16 LE
                try
                {
                    string content = Encoding.Unicode.GetString(bytes);
                    lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    selectedEncoding = Encoding.Unicode;
                }
                catch
                {
                    // 解析失败，继续尝试其他编码
                }
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                // UTF-16 BE
                try
                {
                    string content = Encoding.BigEndianUnicode.GetString(bytes);
                    lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    selectedEncoding = Encoding.BigEndianUnicode;
                }
                catch
                {
                    // 解析失败，继续尝试其他编码
                }
            }

            // 如果没有BOM或BOM解析失败，尝试多种编码格式
            if (lines == null || lines.Length == 0)
            {
                // 优先尝试UTF-8，然后是ANSI和其他编码
                var encodings = new List<Encoding>
                {
                    Encoding.UTF8,           // 优先尝试UTF-8
                    Encoding.Default,         // ANSI编码
                    Encoding.GetEncoding(936), // GB2312
                    Encoding.GetEncoding(1252), // Windows-1252
                };

                foreach (var encoding in encodings)
                {
                    try
                    {
                        string content = encoding.GetString(bytes);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            selectedEncoding = encoding;
                            if (lines.Length > 0)
                                break;
                        }
                    }
                    catch
                    {
                        // 编码错误，尝试下一种编码
                    }
                }
            }

            // 最后的后备方案：使用默认编码读取
            if (lines == null || lines.Length == 0)
            {
                try
                {
                    string content = Encoding.Default.GetString(bytes);
                    lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    selectedEncoding = Encoding.Default;
                }
                catch
                {
                    return;
                }
            }

            if (lines == null || lines.Length == 0)
                return;

            var headers = ParseCsvLine(lines[0]);
            // 处理重复列名
            var uniqueHeaders = GetUniqueHeaders(headers);
            csvInfo.Headers = uniqueHeaders;

            foreach (var header in uniqueHeaders)
            {
                dataTable.Columns.Add(header, typeof(string));
            }

            // 批量添加数据行
            int rowCount = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var values = ParseCsvLine(lines[i]);
                var row = dataTable.NewRow();

                for (int j = 0; j < Math.Min(values.Count, headers.Count); j++)
                {
                    row[j] = values[j];
                }

                dataTable.Rows.Add(row);
                rowCount++;
            }

            csvInfo.Data = dataTable;
            csvInfo.IsDataLoaded = true;
        }

        private void LoadLargeCsvFileData(CsvFileInfo csvInfo, DataTable dataTable)
        {
            // 对于大文件，使用流式读取和编码检测
            Encoding selectedEncoding = null;
            string[] encodingsToTry = { "utf-8", "gb2312", "windows-1252", "utf-16" };
            bool success = false;

            foreach (var encodingName in encodingsToTry)
            {
                try
                {
                    Encoding encoding = Encoding.GetEncoding(encodingName);
                    using (var reader = new StreamReader(csvInfo.FilePath, encoding))
                    {
                        // 读取表头
                        string headerLine = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(headerLine))
                            continue;

                        var headers = ParseCsvLine(headerLine);
                        // 处理重复列名
                        var uniqueHeaders = GetUniqueHeaders(headers);
                        csvInfo.Headers = uniqueHeaders;

                        foreach (var header in uniqueHeaders)
                        {
                            dataTable.Columns.Add(header, typeof(string));
                        }

                        // 读取数据行
                        string line;
                        int rowCount = 0;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            var values = ParseCsvLine(line);
                            var row = dataTable.NewRow();

                            for (int j = 0; j < Math.Min(values.Count, headers.Count); j++)
                            {
                                row[j] = values[j];
                            }

                            dataTable.Rows.Add(row);
                            rowCount++;
                        }

                        selectedEncoding = encoding;
                        success = true;
                        break;
                    }
                }
                catch
                {
                    // 编码错误，尝试下一种编码
                    dataTable.Clear();
                    dataTable.Columns.Clear();
                    continue;
                }
            }

            if (success)
            {
                csvInfo.Data = dataTable;
                csvInfo.IsDataLoaded = true;
            }
        }

        public CsvFileInfo LoadCsvFile(string filePath)
        {
            var csvInfo = LoadCsvFileMetadata(filePath);
            if (csvInfo != null)
            {
                LoadCsvFileData(csvInfo);
            }
            return csvInfo;
        }



        public List<SearchResult> SearchByContent(List<CsvFileInfo> csvFiles, string keyword)
        {
            var results = new List<SearchResult>();

            if (csvFiles == null || csvFiles.Count == 0)
                return results;

            string lowerKeyword = keyword?.ToLower();
            bool hasSearchKeyword = !string.IsNullOrWhiteSpace(lowerKeyword);

            // 使用并行处理加速搜索，但保持顺序
            var searchResults = new SearchResult[csvFiles.Count];
            System.Threading.Tasks.Parallel.For(0, csvFiles.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount * 2 }, i =>
            {
                var csvFile = csvFiles[i];
                
                // 对于无搜索关键词的情况，不加载文件数据，只返回文件列表
                if (!hasSearchKeyword)
                {
                    var result = new SearchResult
                    {
                        FilePath = csvFile.FilePath,
                        FileName = csvFile.FileName,
                        FolderPath = csvFile.FolderPath,
                        MatchedHeaders = csvFile.Headers,
                        HeaderSignature = GetHeaderSignature(csvFile.Headers),
                        FilteredData = null // 不加载数据，延迟到用户选择时加载
                    };
                    searchResults[i] = result;
                    return;
                }

                // 延迟加载：只有在需要时才加载文件数据
                if (!csvFile.IsDataLoaded)
                {
                    LoadCsvFileData(csvFile);
                }

                // 如果数据加载失败，跳过该文件
                if (csvFile.Data == null || csvFile.Data.Rows.Count == 0)
                    return;

                var matchingRows = new List<DataRow>();

                // 优化搜索逻辑
                foreach (DataRow row in csvFile.Data.Rows)
                {
                    bool found = false;
                    foreach (DataColumn column in csvFile.Data.Columns)
                    {
                        var cellValue = row[column]?.ToString()?.ToLower();
                        if (cellValue != null && cellValue.Contains(lowerKeyword))
                        {
                            matchingRows.Add(row);
                            found = true;
                            break;
                        }
                    }
                    if (!found && matchingRows.Count >= 1000) // 限制匹配行数，避免内存占用过大
                        break;
                }

                if (matchingRows.Count > 0)
                {
                    var result = new SearchResult
                    {
                        FilePath = csvFile.FilePath,
                        FileName = csvFile.FileName,
                        FolderPath = csvFile.FolderPath,
                        MatchedHeaders = csvFile.Headers,
                        HeaderSignature = GetHeaderSignature(csvFile.Headers)
                    };

                    var filteredTable = csvFile.Data.Clone();
                    // 批量导入匹配的行，限制行数
                    const int MAX_MATCHING_ROWS = 1000;
                    int importedRows = 0;
                    foreach (var row in matchingRows)
                    {
                        if (importedRows >= MAX_MATCHING_ROWS)
                            break;
                        filteredTable.ImportRow(row);
                        importedRows++;
                    }

                    result.FilteredData = filteredTable;
                    searchResults[i] = result;
                }
            });

            // 按原始顺序收集结果
            foreach (var result in searchResults)
            {
                if (result != null)
                {
                    results.Add(result);
                }
            }

            return results;
        }

        public Dictionary<string, List<SearchResult>> GroupResultsByHeaderSignature(List<SearchResult> results)
        {
            var groups = new Dictionary<string, List<SearchResult>>();

            foreach (var result in results)
            {
                string signature = result.HeaderSignature;
                if (!groups.ContainsKey(signature))
                {
                    groups[signature] = new List<SearchResult>();
                }
                groups[signature].Add(result);
            }

            return groups;
        }

        private string GetHeaderSignature(List<string> headers)
        {
            var sorted = headers.OrderBy(h => h).ToList();
            return string.Join("|", sorted);
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString().Trim());
            return result;
        }

        private List<string> GetUniqueHeaders(List<string> headers)
        {
            var uniqueHeaders = new List<string>();
            var headerCounts = new Dictionary<string, int>();

            // 首先统计每个列名出现的次数
            foreach (var header in headers)
            {
                if (headerCounts.ContainsKey(header))
                {
                    headerCounts[header]++;
                }
                else
                {
                    headerCounts[header] = 1;
                }
            }

            // 然后处理每个列名，为重复的列名添加标注
            var headerOccurrences = new Dictionary<string, int>();
            foreach (var header in headers)
            {
                string originalHeader = header;
                string uniqueHeader = header;
                
                // 只有当列名出现次数大于1时才添加标注
                if (headerCounts[header] > 1)
                {
                    if (!headerOccurrences.ContainsKey(header))
                    {
                        headerOccurrences[header] = 1;
                    }
                    else
                    {
                        headerOccurrences[header]++;
                    }
                    
                    uniqueHeader = $"{originalHeader}(重复项 {headerOccurrences[header]})";
                }

                uniqueHeaders.Add(uniqueHeader);
            }

            return uniqueHeaders;
        }
    }
}
