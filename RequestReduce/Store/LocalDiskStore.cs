﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using RequestReduce.Configuration;
using RequestReduce.Module;
using RequestReduce.Utilities;
using RequestReduce.IOC;
using RequestReduce.ResourceTypes;

namespace RequestReduce.Store
{
    public class LocalDiskStore : IStore
    {
        protected readonly IFileWrapper FileWrapper;
        private readonly IRRConfiguration configuration;
        private readonly IUriBuilder uriBuilder;
        protected IReductionRepository ReductionRepository;
        private FileSystemWatcher watcher;

        public LocalDiskStore(IFileWrapper fileWrapper, IRRConfiguration configuration, IUriBuilder uriBuilder, IReductionRepository reductionRepository)
        {
            FileWrapper = fileWrapper;
            this.configuration = configuration;
            configuration.PhysicalPathChange += SetupWatcher;
            this.uriBuilder = uriBuilder;
            ReductionRepository = reductionRepository;
            if(configuration.IsFullTrust)
                SetupWatcher();
        }

        protected LocalDiskStore()
        {
        }

        protected virtual void SetupWatcher()
        {
            if (string.IsNullOrEmpty(configuration.SpritePhysicalPath)) return;
            watcher = new FileSystemWatcher();
            if (configuration != null)
            {
                RRTracer.Trace("Setting up File System Watcher for {0}", configuration.SpritePhysicalPath);
                watcher.Path = configuration.SpritePhysicalPath;
            }
            watcher.IncludeSubdirectories = true;
            watcher.Filter = "*RequestReduce*";
            watcher.Created += OnChange;
            watcher.Deleted += OnChange;
            watcher.Changed += OnChange;
            watcher.EnableRaisingEvents = true;
        }

        private void OnChange(object sender, FileSystemEventArgs e)
        {
            var path = e.FullPath;
            RRTracer.Trace("watcher watched {0}", path);
            var guid = uriBuilder.ParseKey(path.Replace('\\', '/'));
            var contentSignature = uriBuilder.ParseSignature(path.Replace('\\', '/'));
            if(guid != Guid.Empty)
            {
                var resourceType = RRContainer.Current.GetAllInstances<IResourceType>().SingleOrDefault(x => path.EndsWith(x.FileName, true, CultureInfo.InvariantCulture));
                if (resourceType != null)
                {
                    RRTracer.Trace("New Content {0} and watched: {1}", e.ChangeType, path);
                    if (e.ChangeType == WatcherChangeTypes.Deleted)
                        ReductionRepository.RemoveReduction(guid);
                    if ((e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Changed))
                        ReductionRepository.AddReduction(guid, uriBuilder.BuildResourceUrl(guid, contentSignature, resourceType.GetType()));
                }
            }
        }

        public virtual void Save(byte[] content, string url, string originalUrls)
        {
            var file = GetFileNameFromConfig(url);
            var sig = uriBuilder.ParseSignature(url);
            var guid = uriBuilder.ParseKey(url);
            FileWrapper.Save(content, file);
            if (!url.ToLower().EndsWith(".png") && ReductionRepository != null)
                ReductionRepository.AddReduction(guid, url);
            RRTracer.Trace("{0} saved to disk.", url);
            var expiredFile = file.Insert(file.IndexOf(sig, StringComparison.Ordinal), "Expired-");
            if (FileWrapper.FileExists(expiredFile))
                FileWrapper.DeleteFile(expiredFile);
        }

        public virtual bool SendContent(string url, HttpResponseBase response)
        {
            var file = GetFileNameFromConfig(url);
            try
            {
                response.TransmitFile(file);
                RRTracer.Trace("{0} transmitted from disk.", url);
                return true;
            }
            catch (FileNotFoundException)
            {
                try
                {
                    response.TransmitFile(file.Insert(file.LastIndexOf('-'), "-Expired"));
                    RRTracer.Trace("{0} was expired and transmitted from disk.", url);
                    return true;
                }
                catch (FileNotFoundException)
                {
                    return false;
                }
            }
        }

        public IDictionary<Guid, string> GetSavedUrls()
        {
            RRTracer.Trace("LocalDiskStore Looking for previously saved content.");
            var dic = new Dictionary<Guid, string>();
            if (configuration == null || string.IsNullOrEmpty(configuration.SpritePhysicalPath))
                return dic;

            var activeFiles = FileWrapper.GetDatedFiles(configuration.SpritePhysicalPath, "*RequestReduce*");
            return (from files in activeFiles
                    where !files.FileName.Contains("-Expired-") && uriBuilder.ParseKey(files.FileName.Replace("\\", "/")) != Guid.Empty
                    group files by uriBuilder.ParseKey(files.FileName.Replace("\\", "/"))
                    into filegroup
                    join files2 in activeFiles on new {k = filegroup.Key, u = filegroup.Max(m => m.CreatedDate)} equals
                        new {k = uriBuilder.ParseKey(files2.FileName.Replace("\\", "/")), u = files2.CreatedDate}
                    select files2.FileName)
                .ToDictionary(file => uriBuilder.ParseKey(file.Replace("\\", "/")),
                              file =>
                              uriBuilder.BuildResourceUrl(uriBuilder.ParseKey(file.Replace("\\", "/")),
                                                          uriBuilder.ParseSignature(file.Replace("\\", "/")),
                                                          RRContainer.Current.GetAllInstances<IResourceType>().
                                                              SingleOrDefault(x => file.EndsWith(x.FileName, true, CultureInfo.InvariantCulture)).GetType()));
        }

        public void Flush(Guid keyGuid)
        {
            if (keyGuid == Guid.Empty)
            {
                var urls = GetSavedUrls();
                foreach (var key in urls.Keys)
                    Flush(key);
            }

            ReductionRepository.RemoveReduction(keyGuid); 
            var files =
                FileWrapper.GetFiles(configuration.SpritePhysicalPath).Where(
                    x => x.Contains(keyGuid.RemoveDashes()) && !x.Contains("Expired"));
            foreach (var file in files)
                FileWrapper.RenameFile(file, file.Replace(keyGuid.RemoveDashes(), keyGuid.RemoveDashes() + "-Expired"));
        }

        protected virtual string GetFileNameFromConfig(string url)
        {
            var fileName = url.ToLower(CultureInfo.InvariantCulture);
            var idx = fileName.IndexOf(configuration.SpriteVirtualPath.ToLower(CultureInfo.InvariantCulture));
            return idx > -1
                       ? fileName.Replace(configuration.SpriteVirtualPath.ToLower(CultureInfo.InvariantCulture),
                                          configuration.SpritePhysicalPath.ToLower(CultureInfo.InvariantCulture)).
                             Substring(idx).Replace('/', '\\')
                       : url;
        }

        public virtual void Dispose()
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            RRTracer.Trace("Local Disk Store Disposed.");
        }


        public string GetUrlByKey(Guid keyGuid, Type resourceType)
        {
            return null;
        }
    }
}
