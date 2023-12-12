using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Path = System.IO.Path;

namespace CloudDownload
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string UpdateUrl = "https://example.com/update/update.zip";
        private const string LocalUpdatePath = "update.zip";
        private const string LocalFilePath = "E:\\230524_095505.s_temp"; // 本地文件路径

        private const string LocalZipFilePath = "https://tipscope-app-image.oss-cn-hangzhou.aliyuncs.com/public/scan/update/MultiScan.zip"; // 云端下载地址
        private const string ExtractedFolderPath = "extracted"; // 解压缩后的文件夹路径

        private CancellationTokenSource cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
           
        }

        

        private bool HttpFileExist(string httpFileUrl)
        {
            WebResponse response = null;
            bool result = false;

            try
            {
                response = WebRequest.Create(httpFileUrl).GetResponse();
                result = response != null;
            }
            catch (Exception)
            {
                result = false;
            }
            finally
            {
                response?.Close();
            }

            return result;
        }

       

        private void UpdateProgressText(int progressPercentage)
        {
            progressText.Text = $"{progressPercentage}%";
        }
        private void ExtractZipFile(string zipFilePath, string targetDirectory)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string entryPath = Path.Combine(targetDirectory, entry.FullName);
                        entryPath = Path.GetFullPath(entryPath);

                        if (!entryPath.StartsWith(targetDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException("目录遍历攻击检测！");
                        }

                        if (File.Exists(entryPath))
                        {
                            File.Delete(entryPath);
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(entryPath));

                        entry.ExtractToFile(entryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解压缩文件时出错：{ex.Message}");
            }
        }

        private  void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 取消下载任务
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }
                Task.Run(() =>
                {
                    Process[] p = Process.GetProcessesByName(LocalUpdatePath);
                    foreach (var item in p)
                    {
                        item.Kill();
                    }
                    Thread.Sleep(200);
                    File.Delete(LocalUpdatePath);
                });

            }
            catch (Exception ex)
            {
                MessageBox.Show($"取消下载时出错：{ex.Message}");
            }
        }



        private async Task DownloadFileWithProgress(string url, string savePath, ProgressBar progressBar, CancellationToken cancellationToken)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    // 发起 HTTP 请求获取流
                    using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        var contentLength = response.Content.Headers.ContentLength;

                        using (var remoteStream = await response.Content.ReadAsStreamAsync())
                        {
                            // 执行下载
                            using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                            {
                                var buffer = new byte[4096];
                                var bytesRead = 0;
                                var totalBytes = 0;

                                do
                                {
                                    // 检查是否取消
                                    cancellationToken.ThrowIfCancellationRequested();

                                    bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                                    if (bytesRead > 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                                        totalBytes += bytesRead;

                                        // 更新进度条
                                        if (contentLength.HasValue && contentLength > 0)
                                        {
                                            var progressPercentage = (int)((double)totalBytes / contentLength.Value * 100);
                                            progressBar.Value = progressPercentage;
                                            if(progressPercentage<1) progressPercentage = 1;
                                            UpdateProgressText(progressPercentage - 1);
                                        }
                                    }
                                } while (bytesRead > 0);
                            }
                        }
                    }

                    // 下载完成后的其他操作（解压缩、删除等）
                    if (!cancellationToken.IsCancellationRequested)
                    {
                       string str=  CalculateMD5(savePath);
                        ExtractZipFile(savePath, AppDomain.CurrentDomain.BaseDirectory);
                        File.Delete(savePath);

                        // 下载完成后更新进度条和文本
                        await Dispatcher.InvokeAsync(() =>
                        {
                            progressBar.Value = progressBar.Maximum;
                            UpdateProgressText(100);

                            MessageBox.Show("压缩包下载并解压成功，保存的 ZIP 文件已删除！");
                        });
                    }
                    else
                    {
                        MessageBox.Show("下载被取消！");
                    }
                }
                catch (OperationCanceledException)
                {
                    MessageBox.Show("下载被取消！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"下载文件时出错：{ex.Message}");
                }
            }
        }



        private async void DownloadAndExtractButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 取消之前的下载任务
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }

                // 创建新的 CancellationTokenSource
                cancellationTokenSource = new CancellationTokenSource();
                if (HttpFileExist(LocalZipFilePath))
                {
                    // 下载文件并显示进度
                    await DownloadFileWithProgress(LocalZipFilePath, LocalUpdatePath, progressBar, cancellationTokenSource.Token);
                }
                else
                {
                    MessageBox.Show("远程文件不存在！");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下载和解压缩文件时出错：{ex.Message}");
            }
        }
        private string CalculateMD5(string filePath)
        {
            using (MD5 md5 = MD5.Create())
            {
                using (FileStream stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = md5.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        private bool CheckInternetConnection()
        {
            try
            {
                // 尝试通过 Ping 命令检测互联网连接
                using (Ping ping = new Ping())
                {
                    // 选择一个可以被访问的互联网地址，例如谷歌的 DNS 服务器
                    string host = "8.8.8.8";

                    // 设置超时时间
                    int timeout = 1000; // 1 秒

                    // 发送 Ping 请求
                    PingReply reply = ping.Send(host, timeout);

                    // 判断 Ping 请求是否成功
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                // 发生异常表示互联网连接失败
                return false;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string jsonString = "{ \"code\": 0, \"data\": { \"serialNumber\":\"asdfasdfas\", \"firewareVer\": \"v1.2\", \"downloadUrl\": \"https://...\", \"fileMd5\": \"asfk3r232113\", \"fileSize\": 13412348932 ,\"MinSupportVer\": \"v1.1\"}, \"msg\": \"success\" }";

            ApiResponse apiResponse = ParseJson(jsonString);

            if (apiResponse.Code == 0)
            {
                // 访问解析后的数据

                Console.WriteLine($"Serial Number: {apiResponse.Data.SerialNumber}");
                Console.WriteLine($"Firmware Version: {apiResponse.Data.FirewareVer}");
                Console.WriteLine($"Download URL: {apiResponse.Data.DownloadUrl}");
                Console.WriteLine($"File MD5: {apiResponse.Data.FileMd5}");
                Console.WriteLine($"File Size: {apiResponse.Data.FileSize}");
                Console.WriteLine($"MinSupportVer:{apiResponse.Data.MinSupportVer}");
            }
            else
            {
                // 解析失败
                Console.WriteLine($"Failed with message: {apiResponse.Msg}");
            }
        }

        public static ApiResponse ParseJson(string jsonString)
        {
            return JsonConvert.DeserializeObject<ApiResponse>(jsonString);
        }

    }
    public class ApiResponse
    {
        public int Code { get; set; }
        public Data Data { get; set; }
        public string Msg { get; set; }
    }

    public class Data
    {
        public string SerialNumber { get; set; }
        public string FirewareVer { get; set; }
        public string DownloadUrl { get; set; }
        public string FileMd5 { get; set; }
        public long FileSize { get; set; }
        public string? MinSupportVer { get; set; }
    }

}
