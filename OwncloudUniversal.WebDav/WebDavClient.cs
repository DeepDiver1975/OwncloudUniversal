﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.UI.Notifications;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using OwncloudUniversal.Shared;
using OwncloudUniversal.WebDav.Model;
using HttpStatusCode = Windows.Web.Http.HttpStatusCode;

namespace OwncloudUniversal.WebDav
{
    public class WebDavClient
    {
        private readonly Uri _serverUrl;
        private readonly NetworkCredential _credential;

        public WebDavClient(Uri serverUrl, NetworkCredential credential)
        {
            _serverUrl = serverUrl;
            _credential = credential;
        }

        public async Task<List<DavItem>> ListFolder(Uri url)
        {
            if (!url.IsAbsoluteUri)
                url = new Uri(_serverUrl, url);
            var propRequest = new WebDavRequest(_credential, url, new HttpMethod("PROPFIND"));
            var response = await propRequest.SendAsync();

            if (response.IsSuccessStatusCode)
            {
                var inputStream = await response.Content.ReadAsInputStreamAsync();
                return XmlParser.ParsePropfind(inputStream.AsStreamForRead());
            }
            throw new WebDavException(response.StatusCode, response.ReasonPhrase, null);
        }

        public async Task Download(Uri url, StorageFile targetFile)
        {
            if (!url.IsAbsoluteUri)
                url = new Uri(_serverUrl, url);
            var downloader = new BackgroundDownloader();
            downloader.ServerCredential = new PasswordCredential(_serverUrl.ToString(), _credential.UserName, _credential.Password);
            var download = downloader.CreateDownload(url, targetFile);
            await download.StartAsync();
        }

        public async Task<DavItem> Upload(Uri url, StorageFile file)
        {
            if (!url.IsAbsoluteUri)
                url = new Uri(_serverUrl, url);

            BackgroundUploader uploader = new BackgroundUploader();
            uploader.Method = "PUT";
            var buffer = CryptographicBuffer.ConvertStringToBinary(_credential.UserName + ":" + _credential.Password, BinaryStringEncoding.Utf8);
            var token = CryptographicBuffer.EncodeToBase64String(buffer);
            var value = new HttpCredentialsHeaderValue("Basic", token);
            uploader.SetRequestHeader("Authorization", value.ToString());

            var upload = uploader.CreateUpload(url, file);
            Progress<UploadOperation> progressCallback = new Progress<UploadOperation>(OnUploadProgressChanged);
            var task = upload.StartAsync().AsTask(progressCallback);
            var task2 = await task.ContinueWith(OnUploadCompleted);
            return await task2;
        }

        private void OnUploadProgressChanged(UploadOperation obj)
        {
            Debug.WriteLine(obj.Progress.Status);
        }

        private async Task<DavItem> OnUploadCompleted(Task<UploadOperation> task)
        {
            var upload = await task;
            return (await ListFolder(upload.RequestedUri)).FirstOrDefault();
        }

        public async Task Delete(Uri url)
        {
            if(!url.IsAbsoluteUri)
                url = new Uri(_serverUrl, url);
            var delRequest = new WebDavRequest(_credential, url, HttpMethod.Delete);
            var response = await delRequest.SendAsync();
            if(response.IsSuccessStatusCode)
                return;
            throw new WebDavException(response.StatusCode, response.ReasonPhrase, null);
        }

        public async Task<DavItem> CreateFolder(Uri url)
        {
            if (!url.IsAbsoluteUri)
                url = new Uri(_serverUrl, url);
            var mkcolRequest = new WebDavRequest(_credential, url, new HttpMethod("MKCOL"));
            var response  = await mkcolRequest.SendAsync();
            if (response.IsSuccessStatusCode)
            {
                var items = await ListFolder(url);
                return items.FirstOrDefault();
            }
            throw new WebDavException(response.StatusCode, response.ReasonPhrase, null);
        }

        public async Task<bool> Exists(Uri url)
        {
            if (!url.IsAbsoluteUri)
                url = new Uri(_serverUrl, url);
            var delRequest = new WebDavRequest(_credential, url, HttpMethod.Head);
            var response = await delRequest.SendAsync();
            return response.StatusCode == HttpStatusCode.Ok;
        }

        public async Task Move(Uri sourceUrl, Uri destinationUrl)
        {
            if (!sourceUrl.IsAbsoluteUri)
                sourceUrl = new Uri(_serverUrl, sourceUrl);
            if (!destinationUrl.IsAbsoluteUri)
                destinationUrl = new Uri(_serverUrl, destinationUrl);
            var headers = new Dictionary<string, string> {{ "Destination", destinationUrl.ToString()}};
            var moveRequest = new WebDavRequest(_credential, sourceUrl, new HttpMethod("MOVE"), Stream.Null, headers);
            var response = await moveRequest.SendAsync();
            if(response.IsSuccessStatusCode)
                return;
            throw new WebDavException(response.StatusCode, response.ReasonPhrase, null);
        }
    }
}
