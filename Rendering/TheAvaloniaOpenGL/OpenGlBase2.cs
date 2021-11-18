using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.OpenGL.Imaging;
using Avalonia.Threading;
using static Avalonia.OpenGL.GlConsts;

namespace Avalonia.OpenGL.Controls
{
    public abstract class OpenGlBase2 : Control
    {
        private IGlContext _context;
        private int _fb, _depthBuffer;
        private OpenGlBitmap _bitmap;
        private IOpenGlBitmapAttachment _attachment;
        private PixelSize _depthBufferSize;
        private bool _glFailed;
        private bool _initialized;
        private readonly OpenGlControlSettings _settings;
        protected GlVersion GlVersion { get; private set; }
        // new
        private bool _disposed;
        private Stopwatch sw = new();
        public float PresentTime { get; private set; }
        public (int, int) PixelSize => (_bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
        // end new
        
        public OpenGlBase2(OpenGlControlSettings settings)
        {
            _settings = settings.Clone();
        }

        public OpenGlBase2() : this(new OpenGlControlSettings())
        {
            
        }


        public sealed override void Render(DrawingContext context)
        {
            if(!EnsureInitialized())
                return;
            
            using (_context.MakeCurrent())
            {
                _context.GlInterface.BindFramebuffer(GL_FRAMEBUFFER, _fb);
                EnsureTextureAttachment();
                EnsureDepthBufferAttachment(_context.GlInterface);
                if(!CheckFramebufferStatus(_context.GlInterface))
                    return;
                
                OnOpenGlRender(_context.GlInterface, _fb);
                // new
                sw.Restart();
                _attachment.Present(); // old
                PresentTime = (float)sw.Elapsed.TotalMilliseconds;
                //end new
            }

            context.DrawImage(_bitmap, new Rect(_bitmap.Size), Bounds);
            base.Render(context);

            if (_settings.ContinuouslyRender)
                // TODO: replace this once we have something like CompositionTargetRendering
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // new
                    if (!_disposed)
                    //end new
                        InvalidateVisual();
                }, DispatcherPriority.Render);
            }
        }
        
        private void CheckError(GlInterface gl)
        {
            int err;
            while ((err = gl.GetError()) != GL_NO_ERROR)
                Console.WriteLine(err);
        }

        void EnsureTextureAttachment()
        {
            _context.GlInterface.BindFramebuffer(GL_FRAMEBUFFER, _fb);
            if (_bitmap == null || _attachment == null || _bitmap.PixelSize != GetPixelSize())
            {
                _attachment?.Dispose();
                _attachment = null;
                _bitmap?.Dispose();
                _bitmap = null;
                _bitmap = new OpenGlBitmap(GetPixelSize(), new Vector(96, 96));
                _attachment = _bitmap.CreateFramebufferAttachment(_context);
            }
        }
        
        void EnsureDepthBufferAttachment(GlInterface gl)
        {
            var size = GetPixelSize();
            if (size == _depthBufferSize && _depthBuffer != 0)
                return;
                    
            gl.GetIntegerv(GL_RENDERBUFFER_BINDING, out var oldRenderBuffer);
            if (_depthBuffer != 0) gl.DeleteRenderbuffers(1, new[] { _depthBuffer });

            var oneArr = new int[1];
            gl.GenRenderbuffers(1, oneArr);
            _depthBuffer = oneArr[0];
            gl.BindRenderbuffer(GL_RENDERBUFFER, _depthBuffer);
            gl.RenderbufferStorage(GL_RENDERBUFFER,
                GlVersion.Type == GlProfileType.OpenGLES ? GL_DEPTH_COMPONENT16 : GL_DEPTH_COMPONENT,
                size.Width, size.Height);
            gl.FramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, _depthBuffer);
            gl.BindRenderbuffer(GL_RENDERBUFFER, oldRenderBuffer);
        }

        void DisposeContextIfNeeded()
        {
            if(_settings.Context == null)
                _context?.Dispose();
            _context = null;
        }
        
        public void Cleanup()
        {
            // new
            _disposed = true;
            // end new
            if (_context != null)
            {
                using (_context.MakeCurrent())
                {
                    var gl = _context.GlInterface;
                    gl.BindTexture(GL_TEXTURE_2D, 0);
                    gl.BindFramebuffer(GL_FRAMEBUFFER, 0);
                    gl.DeleteFramebuffers(1, new[] { _fb });
                    gl.DeleteRenderbuffers(1, new[] { _depthBuffer });
                    _attachment?.Dispose();
                    _attachment = null;
                    _bitmap?.Dispose();
                    _bitmap = null;
                    
                    try
                    {
                        if (_initialized)
                        {
                            _initialized = false;
                            OnOpenGlDeinit(_context.GlInterface, _fb);
                        }
                    }
                    finally
                    {
                        DisposeContextIfNeeded();
                    }
                }
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Cleanup();
            base.OnDetachedFromVisualTree(e);
        }

        private bool EnsureInitializedCore()
        {
            if (_context != null)
                return true;
            
            if (_glFailed)
                return false;
            
            var feature = AvaloniaLocator.Current.GetService<IPlatformOpenGlInterface>();
            if (feature == null)
                return false;

            try
            {
                if (_settings.Context != null)
                    _context = _settings.Context;
                else if (_settings.ContextFactory != null)
                {
                    _context = _settings.ContextFactory();
                    if (_context == null)
                        throw new InvalidOperationException("Custom OpenGL context factory returned null");
                }
                else if (feature.CanShareContexts)
                    _context = feature.CreateSharedContext();
                else
                    _context = feature.CreateOSTextureSharingCompatibleContext(null, new List<GlVersion>
                    {
                        // We are asking to create something usable. For more customization one will have to
                        // use a custom factory, I guess
                        // We probably also want to allow to customize that from settings later
                        new GlVersion(GlProfileType.OpenGL, 3, 2, true),
                        new GlVersion(GlProfileType.OpenGLES, 3, 0),
                        new GlVersion(GlProfileType.OpenGLES, 2, 0),
                        new GlVersion(GlProfileType.OpenGL, 2, 0)
                    });
            }
            catch (Exception e)
            {
                Logger.TryGet(LogEventLevel.Error, "OpenGL")?.Log("OpenGlControlBase",
                    "Unable to initialize OpenGL: unable to create additional OpenGL context: {exception}", e);
                return false;
            }

            GlVersion = _context.Version;
            try
            {
                _bitmap = new OpenGlBitmap(GetPixelSize(), new Vector(96, 96));
                if (!_bitmap.SupportsContext(_context))
                {
                    Logger.TryGet(LogEventLevel.Error, "OpenGL")?.Log("OpenGlControlBase",
                        "Unable to initialize OpenGL: unable to create OpenGlBitmap: OpenGL context is not compatible");
                    return false;
                }
            }
            catch (Exception e)
            {
                DisposeContextIfNeeded();
                Logger.TryGet(LogEventLevel.Error, "OpenGL")?.Log("OpenGlControlBase",
                    "Unable to initialize OpenGL: unable to create OpenGlBitmap: {exception}", e);
                return false;
            }

            using (_context.MakeCurrent())
            {
                try
                {
                    _depthBufferSize = GetPixelSize();
                    var gl = _context.GlInterface;
                    var oneArr = new int[1];
                    gl.GenFramebuffers(1, oneArr);
                    _fb = oneArr[0];
                    gl.BindFramebuffer(GL_FRAMEBUFFER, _fb);
                    
                    EnsureDepthBufferAttachment(gl);
                    EnsureTextureAttachment();

                    return CheckFramebufferStatus(gl);
                }
                catch(Exception e)
                {
                    Logger.TryGet(LogEventLevel.Error, "OpenGL")?.Log("OpenGlControlBase",
                        "Unable to initialize OpenGL FBO: {exception}", e);
                    return false;
                }
            }
        }

        private bool CheckFramebufferStatus(GlInterface gl)
        {
            var status = gl.CheckFramebufferStatus(GL_FRAMEBUFFER);
            if (status != GL_FRAMEBUFFER_COMPLETE)
            {
                int code;
                int lastError = 0;
                while ((code = gl.GetError()) != 0)
                {
                    if (lastError == code)
                        continue;
                    else 
                        lastError = code;
                    Logger.TryGet(LogEventLevel.Error, "OpenGL")?.Log("OpenGlControlBase",
                        "Unable to initialize OpenGL FBO: {code}", code);
                }

                return false;
            }

            return true;
        }

        private bool EnsureInitialized()
        {
            // new
            if (_disposed)
                return false;
            // end new
            if (_initialized)
                return true;
            _glFailed = !(_initialized = EnsureInitializedCore());
            if (_glFailed)
                return false;
            using (_context.MakeCurrent())
                OnOpenGlInit(_context.GlInterface, _fb);
            return true;
        }
        
        private PixelSize GetPixelSize()
        {
            var scaling = 1;//VisualRoot.RenderScaling;
            return new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)),
                Math.Max(1, (int)(Bounds.Height * scaling)));
        }


        protected virtual void OnOpenGlInit(GlInterface gl, int fb)
        {
            
        }

        protected virtual void OnOpenGlDeinit(GlInterface gl, int fb)
        {
            
        }
        
        protected abstract void OnOpenGlRender(GlInterface gl, int fb);
    }
}