#region License
/*
Microsoft Public License (Ms-PL)
MonoGame - Copyright © 2009 The MonoGame Team

All rights reserved.

This license governs use of the accompanying software. If you use the software, you accept this license. If you do not
accept the license, do not use the software.

1. Definitions
The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same meaning here as under 
U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the software.
A "contributor" is any person that distributes its contribution under this license.
"Licensed patents" are a contributor's patent claims that read directly on its contribution.

2. Grant of Rights
(A) Copyright Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, 
each contributor grants you a non-exclusive, worldwide, royalty-free copyright license to reproduce its contribution, prepare derivative works of its contribution, and distribute its contribution or any derivative works that you create.
(B) Patent Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, 
each contributor grants you a non-exclusive, worldwide, royalty-free license under its licensed patents to make, have made, use, sell, offer for sale, import, and/or otherwise dispose of its contribution in the software or derivative works of the contribution in the software.

3. Conditions and Limitations
(A) No Trademark License- This license does not grant you rights to use any contributors' name, logo, or trademarks.
(B) If you bring a patent claim against any contributor over patents that you claim are infringed by the software, 
your patent license from such contributor to the software ends automatically.
(C) If you distribute any portion of the software, you must retain all copyright, patent, trademark, and attribution 
notices that are present in the software.
(D) If you distribute any portion of the software in source code form, you may do so only under this license by including 
a complete copy of this license with your distribution. If you distribute any portion of the software in compiled or object 
code form, you may only do so under a license that complies with this license.
(E) The software is licensed "as-is." You bear the risk of using it. The contributors give no express warranties, guarantees
or conditions. You may have additional consumer rights under your local laws which this license cannot change. To the extent
permitted under your local laws, the contributors exclude the implied warranties of merchantability, fitness for a particular
purpose and non-infringement.
*/
#endregion License

using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using Path = System.IO.Path;

namespace Microsoft.Xna.Framework.Content
{
    public partial class ContentManager : IDisposable
    {
        private string rootDirectory = string.Empty;
		private bool disposed;		                        
		protected IServiceProvider serviceProvider;
        protected IGraphicsDeviceService graphicsDeviceService;
        protected Dictionary<string, object> loadedAssets = new Dictionary<string, object>();

        private static object ContentManagerLock = new object();
        private static List<ContentManager> ContentManagers = new List<ContentManager>();

        private static void AddContentManager(ContentManager contentManager)
        {
            lock (ContentManagerLock)
            {
                ContentManagers.Add(contentManager);
            }
        }

        private static void RemoveContentManager(ContentManager contentManager)
        {
            lock (ContentManagerLock)
            {
                if(ContentManagers.Contains(contentManager))
                    ContentManagers.Remove(contentManager);
            }
        }

        internal static void ReloadAllContent()
        {
            lock (ContentManagerLock)
            {
                foreach (var contentManager in ContentManagers)
                {
                    contentManager.ReloadContent();
                }
            }
        }

        public ContentManager(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException("serviceProvider");
            }
            this.serviceProvider = serviceProvider;
            AddContentManager(this);
        }

        public ContentManager(IServiceProvider serviceProvider, string rootDirectory)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException("serviceProvider");
            }
            if (rootDirectory == null)
            {
                throw new ArgumentNullException("rootDirectory");
            }
            this.RootDirectory = rootDirectory;
            this.serviceProvider = serviceProvider;
            AddContentManager(this);
        }

        public void Dispose()
        {
            Dispose(true);
            // Tell the garbage collector not to call the finalizer
            // since all the cleanup will already be done.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                Unload();
                disposed = true;
            }
        }

		public IServiceProvider ServiceProvider
		{
			get
			{
				return serviceProvider;
			}
		}

		public string RootDirectory
		{
			get
			{
				return rootDirectory;
			}
			set
			{
				// TODO: Shouldn't this unload all the existing 
				// content if the value changes?
				rootDirectory = value;
			}
		}

		protected void EnsureGraphicsDeviceService()
		{
			if (this.graphicsDeviceService == null)
			{
				this.graphicsDeviceService = serviceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
				if (this.graphicsDeviceService == null)
				{
					throw new InvalidOperationException("No Graphics Device Service");
				}
			}
		}

        public virtual T Load<T>(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new ArgumentNullException("assetName");
            }

            if (disposed)
            {
                throw new ObjectDisposedException("ContentManager");
            }

            // Check for a previously loaded asset first
            object asset = null;
            if (loadedAssets.TryGetValue(assetName, out asset))
            {
                if (asset is T)
                {
                    return (T)asset;
                }
            }

            // Load the asset.
            var result = ReadAsset<T>(assetName, null);

            // Cache the result.
            if (!loadedAssets.ContainsKey(assetName))
            {
                loadedAssets.Add(assetName, result);
            }

            return result;
        }

        protected T ReadAsset<T>(string assetName, Action<IDisposable> recordDisposableObject)
        {
            EnsureGraphicsDeviceService();

			T result = ReadAsset<T>(assetName);

			IDisposable resultAsDisposable = result as IDisposable;

            if (recordDisposableObject != null && resultAsDisposable != null)
                recordDisposableObject(resultAsDisposable);

            return result;
        }

		protected virtual T ReadAsset<T>(string assetName)
		{
			Type assetType = typeof(T);
			string assetFileName = GetAssetFileName(assetName, assetType);

			object result = null;
			
			if (Path.GetExtension(assetFileName).ToLower() == ".xnb")
			{
				// Load a XNB file
				Stream stream = OpenStream(assetFileName);
				try
				{
					using (BinaryReader xnbReader = new BinaryReader(stream))
					{
						// The first 4 bytes should be the "XNB" header. i use that to detect an invalid file
						byte x = xnbReader.ReadByte();
						byte n = xnbReader.ReadByte();
						byte b = xnbReader.ReadByte();
						byte platform = xnbReader.ReadByte();
						
						if (x != 'X' || n != 'N' || b != 'B' ||
						    !(platform == 'w' || platform == 'x' || platform == 'm'))
						{
							throw new ContentLoadException("Asset does not appear to be a valid XNB file. Did you process your content for Windows?");
						}
						
						byte version = xnbReader.ReadByte();
						byte flags = xnbReader.ReadByte();
						
						bool compressed = (flags & 0x80) != 0;
						if (version != 5 && version != 4)
						{
							throw new ContentLoadException("Invalid XNB version");
						}
						
						// The next int32 is the length of the XNB file
						int xnbLength = xnbReader.ReadInt32();
						
						ContentReader reader;
						if (compressed)
						{
							
							LzxDecoder dec = new LzxDecoder(16);  							
							//decompress the xnb
							//thanks to ShinAli (https://bitbucket.org/alisci01/xnbdecompressor)
							int compressedSize = xnbLength - 14;
							int decompressedSize = xnbReader.ReadInt32();
							int newFileSize = decompressedSize + 10;
							
							MemoryStream decompressedStream = new MemoryStream(decompressedSize);
							
							int decodedBytes = 0;
							int pos = 0;							
							
#if ANDROID
							// Android native stream does not support the Position property. LzxDecoder.Decompress also uses
							// Seek.  So we read the entirity of the stream into a memory stream and replace stream with the
							// memory stream.
							MemoryStream memStream = new MemoryStream();
							stream.CopyTo(memStream);
							memStream.Seek(0, SeekOrigin.Begin);
							stream.Dispose();
							stream = memStream;
							pos = -14;
#endif
							
							while (pos < compressedSize)
							{
								// let's seek to the correct position
								// The stream should already be in the correct position, and seeking can be slow
								stream.Seek(pos + 14, SeekOrigin.Begin);
								int hi = stream.ReadByte();
								int lo = stream.ReadByte();
								int block_size = (hi << 8) | lo;
								int frame_size = 0x8000;
								if (hi == 0xFF)
								{
									hi = lo;
									lo = (byte)stream.ReadByte();
									frame_size = (hi << 8) | lo;
									hi = (byte)stream.ReadByte();
									lo = (byte)stream.ReadByte();
									block_size = (hi << 8) | lo;
									pos += 5;
								}
								else
									pos += 2;
								
								if (block_size == 0 || frame_size == 0)
									break;
								
								int lzxRet = dec.Decompress(stream, block_size, decompressedStream, frame_size);
								pos += block_size;
								decodedBytes += frame_size;
							}
							
							if (decompressedStream.Position != decompressedSize)
							{
								throw new ContentLoadException(
									String.Format("Decompression of asset '{0}' failed. Try decompressing with nativeDecompressXnb first.", assetName));
							}
							
							decompressedStream.Seek(0, SeekOrigin.Begin);
							reader = new ContentReader(this, decompressedStream, this.graphicsDeviceService.GraphicsDevice, assetName);
						}
						else
						{
							reader = new ContentReader(this, stream, this.graphicsDeviceService.GraphicsDevice, assetName);
						}
						
						using (reader)
						{
							result = reader.ReadAsset<T>();
						}
					}
				}
				finally
				{
					if (stream != null)
					{
						stream.Dispose();
					}
				}
			}
			else
			{
				if (assetType == typeof(Texture2D))
				{
#if IPHONE
					Texture2D texture = Texture2D.FromFile(graphicsDeviceService.GraphicsDevice, assetFileName);
					texture.Name = assetName;
					result = texture;
#else
					using (Stream assetStream = OpenStream(assetName))
					{
						Texture2D texture = Texture2D.FromFile(graphicsDeviceService.GraphicsDevice, assetStream);
						texture.Name = assetName;
						result = texture;
					}
#endif
				}
				else if (assetType == typeof(SpriteFont))
				{
					//result = new SpriteFont(Texture2D.FromFile(graphicsDeviceService.GraphicsDevice,assetName), null, null, null, 0, 0.0f, null, null);
					throw new NotImplementedException();
				}
				else if (assetType == typeof(Song))
				{
					result = new Song(assetFileName);
				}
				else if (assetType == typeof(SoundEffect))
				{
					result = new SoundEffect(assetFileName);
				}
				else if (assetType == typeof(Video))
				{
					result = new Video(assetFileName);
				}
				else if (assetType == typeof(Effect))
				{
					result = new Effect(graphicsDeviceService.GraphicsDevice, assetFileName);
				}
			}

			return (T)result;
		}

        protected void ReloadContent()
        {
            foreach (var asset in loadedAssets)
            {
                ReloadAsset(asset.Key, asset.Value);
            }
        }

        protected virtual void ReloadAsset(string assetName, object currentAsset)
        {
			EnsureGraphicsDeviceService();

			string assetFileName = GetAssetFileName(assetName, currentAsset.GetType());

            if (Path.GetExtension(assetFileName).ToLower() != ".xnb")
            {
                if (currentAsset is Texture2D)
                {
                    using (Stream assetStream = OpenStream(assetFileName))
                    {
                        var asset = (Texture2D)currentAsset;
                        asset.Reload(assetStream);
                    }
                }
                else if (currentAsset is SpriteFont)
                {
                }
                else if (currentAsset is Song)
                {
                }
                else if (currentAsset is SoundEffect)
                {
                }
                else if (currentAsset is Video)
                {
                }
                else if (currentAsset is Effect)
                {
                }
            }
        }

        public virtual void Unload()
        {
            // Look for disposable assets.
            foreach (var pair in loadedAssets)
            {
                var disposable = pair.Value as IDisposable;
                if (disposable != null )
                    disposable.Dispose();
            }

            RemoveContentManager(this);
            loadedAssets.Clear();
        }

		protected string GetAssetFileName(string assetName, Type assetType)
		{
			var assetFileName = GetFilename(assetName);

			// Get the real file name
			if (assetType == typeof(Curve))
			{				
				assetFileName = CurveReader.Normalize(assetFileName);
			} else if (assetType == typeof(Texture2D))
			{
				assetFileName = Texture2DReader.Normalize(assetFileName);
			} else if (assetType == typeof(SpriteFont))
			{
				assetFileName = SpriteFontReader.Normalize(assetFileName);
			} else if (assetType == typeof(Effect))
			{
				assetFileName = Effect.Normalize(assetFileName);
			} else if (assetType == typeof(Song))
			{
				assetFileName = SongReader.Normalize(assetFileName);
			} else if (assetType == typeof(SoundEffect))
			{
				assetFileName = SoundEffectReader.Normalize(assetFileName);
			} else if (assetType == typeof(Video))
			{
				assetFileName = Video.Normalize(assetFileName);
			}

			if (string.IsNullOrEmpty(assetFileName))
			{
				throw new ContentLoadException ("Could not determine file name for '" + assetName + "' asset!");
			}

			if (!Path.HasExtension(assetFileName))
			{
				assetFileName = assetFileName + ".xnb";
			}

			return assetFileName;
		}
    }
}

