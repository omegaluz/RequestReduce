﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using RequestReduce.Configuration;
using RequestReduce.Utilities;

namespace RequestReduce.Reducer
{
    public class SpriteContainer : ISpriteContainer
    {
        private readonly IList<Bitmap> images = new List<Bitmap>();
        private readonly IWebClientWrapper webClientWrapper;

        public SpriteContainer(IRRConfiguration config, IWebClientWrapper webClientWrapper)
        {
            this.webClientWrapper = webClientWrapper;
            var guid = Guid.NewGuid().ToString();
            Url = string.Format("{0}/{1}.png", config.SpriteVirtualPath, guid);
            FilePath = string.Format("{0}\\{1}.png", config.SpritePhysicalPath, guid);
        }

        public void AddImage (BackgroungImageClass image)
        {
            var imageBytes = webClientWrapper.DownloadBytes(image.ImageUrl);
            Bitmap bitmap = null;
            using (var originalBitmap = new Bitmap(new MemoryStream(imageBytes)))
            {
                using (var writer = new SpriteWriter(image.Width ?? originalBitmap.Width, image.Height ?? originalBitmap.Height, null))
                {
                    var width = image.Width ?? originalBitmap.Width;
                    if (width > originalBitmap.Width)
                        width = originalBitmap.Width;
                    var height = image.Height ?? originalBitmap.Height;
                    if (height > originalBitmap.Height)
                        height = originalBitmap.Height;
                    var x = image.XOffset.Offset < 0 ? Math.Abs(image.XOffset.Offset) : 0;
                    var y = image.YOffset.Offset < 0 ? Math.Abs(image.YOffset.Offset) : 0;

                    writer.WriteImage(originalBitmap.Clone(new Rectangle(x, y, width, height), originalBitmap.PixelFormat));
                    bitmap = writer.SpriteImage;
                }
            }
            images.Add(bitmap);
            Size += imageBytes.Length;
            Width += bitmap.Width;
            if (Height < bitmap.Height) Height = bitmap.Height;
        }

        public string FilePath { get; set; }
        public string Url { get; set; }
        public int Size { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public IEnumerator<Bitmap> GetEnumerator()
        {
            return images.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Dispose()
        {
            images.ToList().ForEach(x => x.Dispose());
        }
    }
}
