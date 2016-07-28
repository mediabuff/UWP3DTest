using SharpDX;
using SharpDX.Mathematics.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using SharpDX.D3DCompiler;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace UWP3DTest
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        D3D11.Device2 device;
        D3D11.DeviceContext deviceContext;
        DXGI.SwapChain2 swapChain;
        D3D11.Texture2D backBufferTexture;
        D3D11.RenderTargetView backBufferView;

        bool isDXInitialized = false;

        float red, green, blue;
        int incr = 1, incg = 1, incb = 1;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void swapChainPanel_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeD3D();
            InitScene();

            var dmp = ThreadPool.RunAsync(LogicLoop);
        }
        private void swapChainPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            Application.Current.Suspending -= Current_Suspending;
            CompositionTarget.Rendering -= CompositionTarget_Rendering;

            using (DXGI.ISwapChainPanelNative nativeObject = ComObject.As<DXGI.ISwapChainPanelNative>(this.swapChainPanel))
                nativeObject.SwapChain = null;
            Utilities.Dispose(ref this.backBufferView);
            Utilities.Dispose(ref this.backBufferTexture);
            Utilities.Dispose(ref this.swapChain);
            Utilities.Dispose(ref this.deviceContext);
            Utilities.Dispose(ref this.device);

            Utilities.Dispose(ref this.triangleVertBuffer);
            Utilities.Dispose(ref this.vs);
            Utilities.Dispose(ref this.ps);
            Utilities.Dispose(ref this.vertLayout);
        }

        private void swapChainPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (isDXInitialized)
            {
                Size2 newSize = new Size2((int)e.NewSize.Width, (int)e.NewSize.Height);

                if (newSize.Width > swapChain.Description1.Width || newSize.Height > swapChain.Description1.Height)
                {
                    Utilities.Dispose(ref this.backBufferView);
                    Utilities.Dispose(ref this.backBufferTexture);

                    swapChain.ResizeBuffers(swapChain.Description.BufferCount, (int)e.NewSize.Width, (int)e.NewSize.Height, swapChain.Description1.Format, swapChain.Description1.Flags);

                    this.backBufferTexture = D3D11.Resource.FromSwapChain<D3D11.Texture2D>(this.swapChain, 0);
                    this.backBufferView = new D3D11.RenderTargetView(this.device, this.backBufferTexture);
                }
                swapChain.SourceSize = newSize;
                deviceContext.Rasterizer.SetViewport(0, 0, (int)e.NewSize.Width, (int)e.NewSize.Height);
            }
        }
        private void Current_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            if (isDXInitialized)
            {
                this.deviceContext.ClearState();
                using (DXGI.Device3 dxgiDevice3 = this.device.QueryInterface<DXGI.Device3>())
                    dxgiDevice3.Trim();
            }
        }

        private void CompositionTarget_Rendering(object sender, object e)
        {
            RenderScene();
        }
        private void LogicLoop(IAsyncAction _a)
        {
            double t = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                if (sw.Elapsed.TotalSeconds - t >= 1.0 / 60)
                {
                    t += 1.0 / 60;
                    UpdateScene();
                }
            }
        }

        private void InitializeD3D()
        {
            using (D3D11.Device defaultDevice = new D3D11.Device(D3D.DriverType.Hardware, D3D11.DeviceCreationFlags.Debug))
                this.device = defaultDevice.QueryInterface<D3D11.Device2>();
            this.deviceContext = this.device.ImmediateContext2;

            DXGI.SwapChainDescription1 swapChainDescription = new DXGI.SwapChainDescription1()
            {
                AlphaMode = DXGI.AlphaMode.Ignore,
                BufferCount = 2,
                Format = DXGI.Format.R8G8B8A8_UNorm,
                Height = (int)(this.swapChainPanel.RenderSize.Height),
                Width = (int)(this.swapChainPanel.RenderSize.Width),
                SampleDescription = new DXGI.SampleDescription(1, 0),
                Scaling = SharpDX.DXGI.Scaling.Stretch,
                Stereo = false,
                SwapEffect = DXGI.SwapEffect.FlipSequential,
                Usage = DXGI.Usage.RenderTargetOutput
            };

            using (DXGI.Device3 dxgiDevice3 = this.device.QueryInterface<DXGI.Device3>())
            using (DXGI.Factory3 dxgiFactory3 = dxgiDevice3.Adapter.GetParent<DXGI.Factory3>())
            {
                DXGI.SwapChain1 swapChain1 = new DXGI.SwapChain1(dxgiFactory3, this.device, ref swapChainDescription);
                this.swapChain = swapChain1.QueryInterface<DXGI.SwapChain2>();
            }

            using (DXGI.ISwapChainPanelNative nativeObject = ComObject.As<DXGI.ISwapChainPanelNative>(this.swapChainPanel))
                nativeObject.SwapChain = this.swapChain;

            this.backBufferTexture = this.swapChain.GetBackBuffer<D3D11.Texture2D>(0);
            this.backBufferView = new D3D11.RenderTargetView(this.device, this.backBufferTexture);
            this.deviceContext.OutputMerger.SetRenderTargets(this.backBufferView);

            deviceContext.Rasterizer.State = new D3D11.RasterizerState(device, new D3D11.RasterizerStateDescription()
            {
                CullMode = D3D11.CullMode.None,
                FillMode = D3D11.FillMode.Solid,
                IsMultisampleEnabled = true
            });
            deviceContext.Rasterizer.SetViewport(0, 0, (int)swapChainPanel.Width, (int)swapChainPanel.Height);

            CompositionTarget.Rendering += CompositionTarget_Rendering;
            Application.Current.Suspending += Current_Suspending;

            isDXInitialized = true;
        }

        D3D11.Buffer triangleVertBuffer;
        D3D11.VertexShader vs;
        D3D11.PixelShader ps;
        D3D11.InputLayout vertLayout;
        RawVector3[] verts;

        private void InitScene()
        {
            D3D11.InputElement[] inputElements = new D3D11.InputElement[]
            {
                new D3D11.InputElement("POSITION", 0, DXGI.Format.R32G32B32_Float, 0)
            };

            using (CompilationResult vsResult = ShaderBytecode.CompileFromFile("vs.hlsl", "main", "vs_4_0"))
            {
                vs = new D3D11.VertexShader(device, vsResult.Bytecode.Data);
                vertLayout = new D3D11.InputLayout(device, vsResult.Bytecode, inputElements);
            }

            using (CompilationResult psResult = ShaderBytecode.CompileFromFile("ps.hlsl", "main", "ps_4_0"))
                ps = new D3D11.PixelShader(device, psResult.Bytecode.Data);

            deviceContext.VertexShader.Set(vs);
            deviceContext.PixelShader.Set(ps);

            verts = new RawVector3[] {
                new RawVector3( 0.0f, 0.5f, 0.5f ),
                new RawVector3( 0.5f, -0.5f, 0.5f ),
                new RawVector3( -0.5f, -0.5f, 0.5f )
            };
            triangleVertBuffer = D3D11.Buffer.Create(device, D3D11.BindFlags.VertexBuffer, verts);

            deviceContext.InputAssembler.InputLayout = vertLayout;
            deviceContext.InputAssembler.PrimitiveTopology = D3D.PrimitiveTopology.TriangleList;
        }
        private void UpdateScene()
        {
            red += incr * 0.01f;
            green += incg * 0.005f;
            blue += incb * 0.002f;

            if (red >= 1.0f || red <= 0.0f)
                incr *= -1;
            if (green >= 1.0f || green <= 0.0f)
                incg *= -1;
            if (blue >= 1.0f || blue <= 0.0f)
                incb *= -1;
        }
        private void RenderScene()
        {
            this.deviceContext.ClearRenderTargetView(this.backBufferView, new RawColor4(red, green, blue, 0));

            deviceContext.InputAssembler.SetVertexBuffers(0,
                new D3D11.VertexBufferBinding(triangleVertBuffer, Utilities.SizeOf<RawVector3>(), 0));
            deviceContext.Draw(verts.Length, 0);

            this.swapChain.Present(0, DXGI.PresentFlags.None);
        }
    }
}
