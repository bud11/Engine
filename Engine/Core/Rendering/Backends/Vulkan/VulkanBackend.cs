




#define ENABLE_VULKAN_DEBUGGING    //<--- enables validation layers etc


namespace Engine.Core;

#if DEBUG
using Engine.Stripped;
#endif


using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Engine.Core.EngineMath;
using static Rendering;





#if IncludeVulkanBackend


public static partial class RenderingBackend
{


    private unsafe class VulkanBackend : IRenderingBackend
    {

        public ShaderFormat RequiredShaderFormat => ShaderFormat.SPIRV;






        private readonly string[] deviceExtensions =
        [
            KhrSwapchain.ExtensionName
        ];



#if DEBUG && ENABLE_VULKAN_DEBUGGING

        private readonly string[] validationLayers =
        [
            "VK_LAYER_KHRONOS_validation",
        ];


        private ExtDebugUtils debugUtils;
        private DebugUtilsMessengerEXT debugMessenger;

#endif




        private Vk VK;


        private Instance instance;

        private KhrSurface khrSurface;
        private SurfaceKHR surface;

        private PhysicalDevice physicalDevice;
        private PhysicalDeviceProperties physicalDeviceProperties;
        private PhysicalDeviceMemoryProperties physicalDeviceMemoryProperties;
        private Device device;

        private Queue graphicsQueue;
        private Queue presentQueue;



        private QueueFamilyIndices QueueFamilies;


        private Framebuffer[] swapChainFramebuffers;
        private ImageView[] swapChainImageViews;
        private Image[] swapChainImages;




        private struct QueueFamilyIndices
        {
            public uint? GraphicsFamily;
            public uint? PresentFamily;

            public readonly bool IsComplete() => GraphicsFamily.HasValue && PresentFamily.HasValue;
        }






        private KhrSwapchain khrSwapChain;
        private SwapchainKHR swapChain;




        private Dictionary<Thread, CommandPool> CommandPools = new();
        private Dictionary<Thread, DescriptorPool> DescriptorPools = new();



        private CommandBuffer RenderThreadCommandBuffer;



        public VulkanBackend(nint sdlwindow)
        {


            CreateInstance();



#if DEBUG && ENABLE_VULKAN_DEBUGGING

            if (!VK.TryGetInstanceExtension(instance, out debugUtils))
                throw new Exception();


            DebugUtilsMessengerCreateInfoEXT createInfo = new();
            PopulateDebugMessengerCreateInfo(ref createInfo);


            if (debugUtils.CreateDebugUtilsMessenger(instance, in createInfo, null, out debugMessenger) != Result.Success)
                throw new Exception();
#endif




            if (!VK.TryGetInstanceExtension(instance, out khrSurface))
                throw new Exception();

            if (!SDL3.SDL.VulkanCreateSurface(sdlwindow, instance.ToHandle().Handle, IntPtr.Zero, out var s))
                throw new Exception();

            surface = new SurfaceKHR((ulong)s);





            PickPhysicalDevice();
            CreateLogicalDevice();


            QueueFamilies = FindQueueFamilies(physicalDevice);

            RenderThreadCommandBuffer = CreateCommandBufferForThisThread();

            CreateSyncObjects();


        }




        private void CreateInstance()
        {
            VK = Vk.GetApi();


#if DEBUG && ENABLE_VULKAN_DEBUGGING
            if (!CheckValidationLayerSupport())
                throw new Exception("validation layers requested, but not available");
#endif


            ApplicationInfo appInfo = new()
            {
                SType = StructureType.ApplicationInfo,
                ApiVersion = Vk.Version10
            };

            InstanceCreateInfo createInfo = new()
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo
            };


            var extensions = GetRequiredExtensions();


            createInfo.EnabledExtensionCount = (uint)extensions.Length;
            createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions); ;



#if DEBUG && ENABLE_VULKAN_DEBUGGING

            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);

            DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
            PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
            createInfo.PNext = &debugCreateInfo;
#else
            createInfo.EnabledLayerCount = 0;
            createInfo.PNext = null;

#endif


            if (VK.CreateInstance(in createInfo, null, out instance) != Result.Success)
                throw new Exception("failed to create instance");


            SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);
        }








        private unsafe string[] GetRequiredExtensions()
        {
            var extensions = SDL3.SDL.VulkanGetInstanceExtensions(out var _);
            
            if (extensions == null) 
                throw new Exception();


#if DEBUG && ENABLE_VULKAN_DEBUGGING

            return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();

#else
            return extensions;
#endif

        }








        private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
        {
            var indices = new QueueFamilyIndices();

            uint queueFamilityCount = 0;
            VK.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, null);

            var queueFamilies = new QueueFamilyProperties[queueFamilityCount];
            fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
            {
                VK.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, queueFamiliesPtr);
            }


            uint i = 0;
            foreach (var queueFamily in queueFamilies)
            {
                if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                {
                    indices.GraphicsFamily = i;
                }

                khrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, surface, out var presentSupport);

                if (presentSupport)
                {
                    indices.PresentFamily = i;
                }

                if (indices.IsComplete())
                {
                    break;
                }

                i++;
            }

            return indices;
        }



        private void PickPhysicalDevice()
        {
            var devices = VK.GetPhysicalDevices(instance);

            foreach (var device in devices)
            {
                if (IsDeviceSuitable(device))
                {
                    physicalDevice = device;
                    break;
                }
            }

            if (physicalDevice.Handle == 0)
                throw new Exception("failed to find a suitable GPU");


            physicalDeviceMemoryProperties = VK.GetPhysicalDeviceMemoryProperties(physicalDevice);
            physicalDeviceProperties = VK.GetPhysicalDeviceProperties(physicalDevice);

        }



        private bool IsDeviceSuitable(PhysicalDevice device)
        {
            var indices = FindQueueFamilies(device);

            bool extensionsSupported = CheckDeviceExtensionsSupport(device);

            bool swapChainAdequate = false;
            if (extensionsSupported)
            {
                var swapChainSupport = QuerySwapChainSupport(device);
                swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
            }

            return indices.IsComplete() && extensionsSupported && swapChainAdequate;
        }

        private bool CheckDeviceExtensionsSupport(PhysicalDevice device)
        {
            uint extentionsCount = 0;
            VK.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, null);

            var availableExtensions = new ExtensionProperties[extentionsCount];
            fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
            {
                VK.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, availableExtensionsPtr);
            }

            var availableExtensionNames = availableExtensions.Select(extension => Marshal.PtrToStringAnsi((nint)extension.ExtensionName)).ToHashSet();

            return deviceExtensions.All(availableExtensionNames.Contains);

        }


        private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
        {
            var details = new SwapChainSupportDetails();

            khrSurface!.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, out details.Capabilities);

            uint formatCount = 0;
            khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref formatCount, null);

            if (formatCount != 0)
            {
                details.Formats = new SurfaceFormatKHR[formatCount];
                fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
                {
                    khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref formatCount, formatsPtr);
                }
            }
            else
            {
                details.Formats = [];
            }

            uint presentModeCount = 0;
            khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, ref presentModeCount, null);

            if (presentModeCount != 0)
            {
                details.PresentModes = new PresentModeKHR[presentModeCount];
                fixed (PresentModeKHR* formatsPtr = details.PresentModes)
                {
                    khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, ref presentModeCount, formatsPtr);
                }

            }
            else
            {
                details.PresentModes = [];
            }

            return details;
        }

        private struct SwapChainSupportDetails
        {
            public SurfaceCapabilitiesKHR Capabilities;
            public SurfaceFormatKHR[] Formats;
            public PresentModeKHR[] PresentModes;
        }







        private void CreateLogicalDevice()
        {
            var indices = FindQueueFamilies(physicalDevice);

            var uniqueQueueFamilies = new[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };
            uniqueQueueFamilies = uniqueQueueFamilies.Distinct().ToArray();

            using var mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
            var queueCreateInfos = (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

            float queuePriority = 1.0f;
            for (int i = 0; i < uniqueQueueFamilies.Length; i++)
            {
                queueCreateInfos[i] = new()
                {
                    SType = StructureType.DeviceQueueCreateInfo,
                    QueueFamilyIndex = uniqueQueueFamilies[i],
                    QueueCount = 1,
                    PQueuePriorities = &queuePriority
                };
            }

            PhysicalDeviceFeatures deviceFeatures = new()
            {
                FillModeNonSolid = true,
            };


            DeviceCreateInfo createInfo = new()
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)uniqueQueueFamilies.Length,
                PQueueCreateInfos = queueCreateInfos,

                PEnabledFeatures = &deviceFeatures,

                EnabledExtensionCount = (uint)deviceExtensions.Length,
                PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions),
            };


            createInfo.EnabledLayerCount = 0;


#if DEBUG && ENABLE_VULKAN_DEBUGGING

            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);
            

#endif



            if (VK.CreateDevice(physicalDevice, in createInfo, null, out device) != Result.Success)
                throw new Exception("failed to create logical device");



            VK.GetDeviceQueue(device, indices.GraphicsFamily!.Value, 0, out graphicsQueue);
            VK.GetDeviceQueue(device, indices.PresentFamily!.Value, 0, out presentQueue);



            //SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);

            SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);


        }









#if DEBUG && ENABLE_VULKAN_DEBUGGING

        private bool CheckValidationLayerSupport()
        {
            uint layerCount = 0;
            VK.EnumerateInstanceLayerProperties(ref layerCount, null);
            var availableLayers = new LayerProperties[layerCount];

            fixed (LayerProperties* availableLayersPtr = availableLayers)
            {
                VK.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
            }


            var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((nint)layer.LayerName)).ToHashSet();

            return validationLayers.All(availableLayerNames.Contains);
        }



        private void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
        {
            createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
            createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
            createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
            createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
        }


        private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {

            string errName = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessageIdName);

            
            //these are special exceptions to allow RTSS and other similar software to work.
            //consider removing these if things break.

            switch (errName)
            {
                case "VUID-VkSwapchainCreateInfoKHR-imageFormat-01778" or "VUID-VkImageViewCreateInfo-usage-02275":
                    return Vk.False;
            }




            string err = $"validation layer:" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage);

            EngineDebug.Print(err);

            if (messageSeverity.HasFlag(DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt))
                throw new Exception(err);

            return Vk.False;
        }



#endif




        private void CreateSyncObjects()
        {
            SemaphoreCreateInfo semaphoreInfo = new()
            {
                SType = StructureType.SemaphoreCreateInfo
            };

            VK.CreateSemaphore(device, &semaphoreInfo, null, out imageAvailableSemaphore);
            VK.CreateSemaphore(device, &semaphoreInfo, null, out renderFinishedSemaphore);

            FenceCreateInfo fenceInfo = new()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit // start signaled so first frame doesn't deadlock
            };

            VK.CreateFence(device, &fenceInfo, null, out FrameFence);


        }



        public SwapchainDetails ConfigureSwapchain(bool UseHDR)
        {


            if (swapChainImageViews != null)
            {
                for (int i = 0; i < swapChainImageViews.Length; i++)
                {
                    VK.DestroyFramebuffer(device, swapChainFramebuffers[i], null);
                    VK.DestroyImageView(device, swapChainImageViews[i], null);
                }

                VK.DestroyRenderPass(device, SwapChainRenderPass, null);
            }





            var swapChainSupport = QuerySwapChainSupport(physicalDevice);

            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats, UseHDR);

            var presentMode = swapChainSupport.PresentModes.Contains(PresentModeKHR.MailboxKhr) ? PresentModeKHR.MailboxKhr : PresentModeKHR.FifoKhr;

            var extent = ChooseSwapExtent(swapChainSupport.Capabilities);



            var imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
                imageCount = swapChainSupport.Capabilities.MaxImageCount;




            SwapchainCreateInfoKHR creatInfo = new()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = surface,

                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.format.Format,
                ImageColorSpace = surfaceFormat.format.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,


                PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = true,   

                OldSwapchain = swapChain,

            };



            var indices = FindQueueFamilies(physicalDevice);
            var pQueueFamilyIndices = stackalloc uint[] { indices.GraphicsFamily.Value, indices.PresentFamily.Value };


            if (indices.GraphicsFamily != indices.PresentFamily)
            {
                creatInfo = creatInfo with
                {
                    ImageSharingMode = SharingMode.Concurrent,
                    QueueFamilyIndexCount = 2,
                    PQueueFamilyIndices = pQueueFamilyIndices,
                };
            }
            else
            {
                creatInfo.ImageSharingMode = SharingMode.Exclusive;
            }



            if (!VK.TryGetDeviceExtension(instance, device, out khrSwapChain))
                throw new Exception();


            if (khrSwapChain.CreateSwapchain(device, in creatInfo, null, out swapChain) != Result.Success)
                throw new Exception();






            swapChainImages = new Image[imageCount];

            fixed (Image* swapChainImagesPtr = swapChainImages)
                khrSwapChain.GetSwapchainImages(device, swapChain, ref imageCount, swapChainImagesPtr);


            var swapChainImageFormat = surfaceFormat.format.Format;


            swapChainImageViews = new ImageView[swapChainImages.Length];

            for (int i = 0; i < swapChainImages.Length; i++)
                swapChainImageViews[i] = CreateImageView(swapChainImages[i], ImageViewType.Type2D, swapChainImageFormat, ImageAspectFlags.ColorBit, 1);



            swapChainFramebuffers = new Framebuffer[swapChainImageViews.Length];


            AttachmentDescription desc = new()
            {
                Format = swapChainImageFormat,
                Samples = SampleCountFlags.Count1Bit,

                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,

                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,

                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            AttachmentReference colorRef = new()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal
            };

            SubpassDescription subpass = new()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,

                ColorAttachmentCount = 1,
                PColorAttachments = &colorRef
            };

            SubpassDependency dependency = new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,

                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,

                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit
            };

            RenderPassCreateInfo cinfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,

                AttachmentCount = 1,
                PAttachments = &desc,

                SubpassCount = 1,
                PSubpasses = &subpass,

                DependencyCount = 1,
                PDependencies = &dependency
            };



            VK.CreateRenderPass(device, &cinfo, null, out SwapChainRenderPass);


            for (int i = 0; i < swapChainImageViews.Length; i++)
            {
                var attachment = swapChainImageViews[i];

                FramebufferCreateInfo framebufferInfo = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = SwapChainRenderPass,
                    AttachmentCount = 1,
                    PAttachments = &attachment,
                    Width = extent.Width,
                    Height = extent.Height,
                    Layers = 1,
                };

                if (VK.CreateFramebuffer(device, in framebufferInfo, null, out swapChainFramebuffers[i]) != Result.Success)
                    throw new Exception();


#if DEBUG

                var name = $"Swapchain Image {i}";

                DebugUtilsObjectNameInfoEXT info = new()
                {
                    SType = StructureType.DebugUtilsObjectNameInfoExt,
                    ObjectType = ObjectType.Image,
                    ObjectHandle = swapChainImages[i].Handle,
                    PObjectName = (byte*)SilkMarshal.StringToPtr(name)
                };

                debugUtils.SetDebugUtilsObjectName(device, &info);

                SilkMarshal.Free((nint)info.PObjectName);

#endif
            }



            return new SwapchainDetails(new Vector2<uint>(extent.Width, extent.Height), surfaceFormat.hdr);

        }


        private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
        {
            var framebufferSize = Window.GetWindowClientArea();

            return new()
            {
                Width = Math.Clamp(framebufferSize.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Height = Math.Clamp(framebufferSize.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height)
            };
        }

        private (SurfaceFormatKHR format, bool hdr) ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats, bool HDR)
        {

            if (HDR)
            {
                foreach (var fmt in availableFormats)
                {
                    if ((fmt.Format == Format.R16G16B16A16Sfloat ||
                         fmt.Format == Format.R16G16B16Sfloat) &&
                        (fmt.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr ||
                         fmt.ColorSpace == ColorSpaceKHR.SpaceExtendedSrgbNonlinearExt))
                    {
                        return (fmt, true);
                    }
                }
            }

            foreach (var fmt in availableFormats)
            {
                if ((fmt.Format == Format.R8G8B8A8Unorm ||
                     fmt.Format == Format.B8G8R8A8Unorm ||
                     fmt.Format == Format.R8G8B8Unorm ||
                     fmt.Format == Format.B8G8R8Unorm) &&
                    fmt.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return (fmt, false);
                }
            }

            throw new Exception();
        }








        private Fence FrameFence;


        private uint CurrentSwapChainImageIndex;

        private Silk.NET.Vulkan.Semaphore imageAvailableSemaphore;
        private Silk.NET.Vulkan.Semaphore renderFinishedSemaphore;




        public void StartFrameRendering()
        {

            VK.ResetFences(device, 1, [FrameFence]);

            // Acquire swapchain image, signal imageAvailableSemaphore
            khrSwapChain.AcquireNextImage(
                device,
                swapChain,
                ulong.MaxValue,
                imageAvailableSemaphore,
                default, // no fence here
                ref CurrentSwapChainImageIndex
            );



            CommandBufferBeginInfo beginInfo = new()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };

            VK.BeginCommandBuffer(RenderThreadCommandBuffer, in beginInfo);

        }



        public void EndFrameRendering()
        {



            VK.EndCommandBuffer(RenderThreadCommandBuffer);

            var cmd = RenderThreadCommandBuffer;

            // Submit work: wait on imageAvailable, signal renderFinished
            PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;


            var avaliablesem = imageAvailableSemaphore;
            var renderfinishsem = renderFinishedSemaphore;


            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &avaliablesem,
                PWaitDstStageMask = &waitStage,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &renderfinishsem
            };




            var swap = swapChain;
            var imageIndex = CurrentSwapChainImageIndex;



            lock (this)
                VK.QueueSubmit(graphicsQueue, 1, &submitInfo, FrameFence);


            VK.WaitForFences(device, 1, [FrameFence], true, ulong.MaxValue);



            // Present waits for renderFinished
            PresentInfoKHR presentInfo = new()
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &renderfinishsem,
                SwapchainCount = 1,
                PSwapchains = &swap,
                PImageIndices = &imageIndex
            };

            khrSwapChain.QueuePresent(graphicsQueue, &presentInfo);
        }








        private uint FindMemoryType(uint typeBits, MemoryPropertyFlags properties)
        {
            for (uint i = 0; i < physicalDeviceMemoryProperties.MemoryTypeCount; i++)
            {
                if ((typeBits & 1 << (int)i) != 0 &&
                    (physicalDeviceMemoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                {
                    return i;
                }
            }
            throw new Exception("No suitable memory type found!");
        }





        private DescriptorPool CreateOrFetchDescriptorPoolForThisThread()
        {

            lock (DescriptorPools)
            {
                if (!DescriptorPools.TryGetValue(Thread.CurrentThread, out var descrPool))
                {
                    const uint MaxSets = 100_000;
                    const uint MaxTexturesPerSet = 10;
                    const uint MaxUBOsPerSet = 10;
                    const uint MaxSSBOsPerSet = 10;


                    Span<DescriptorPoolSize> poolSizes =
                    [

                        //UBO
                        new DescriptorPoolSize
                        {
                            Type = DescriptorType.UniformBufferDynamic,
                            DescriptorCount = MaxUBOsPerSet * MaxSets
                        },


                         //SSBO
                        new DescriptorPoolSize
                        {
                            Type = DescriptorType.StorageBufferDynamic,
                            DescriptorCount = MaxSSBOsPerSet * MaxSets
                        },


                        //TEXTURE
                        new DescriptorPoolSize
                        {
                            Type = DescriptorType.CombinedImageSampler,
                            DescriptorCount = MaxTexturesPerSet * MaxSets
                        }

                    ];



                    fixed (DescriptorPoolSize* p = poolSizes)
                    {
                        DescriptorPoolCreateInfo poolInfo = new()
                        {
                            SType = StructureType.DescriptorPoolCreateInfo,
                            PoolSizeCount = (uint)poolSizes.Length,
                            PPoolSizes = p,

                            MaxSets = MaxSets,

                            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                        };


                        if (VK.CreateDescriptorPool(device, &poolInfo, null, out descrPool) != Result.Success)
                            throw new Exception("failed to create descriptor pool");
                    }

                    DescriptorPools[Thread.CurrentThread] = descrPool;
                }


                return DescriptorPools[Thread.CurrentThread];
            }

        }








        private CommandBuffer CreateCommandBufferForThisThread()
        {
            CommandPool cmdPool;


            lock (CommandPools)
            {
                if (!CommandPools.TryGetValue(Thread.CurrentThread, out cmdPool))
                {

                    CommandPoolCreateInfo poolInfo = new()
                    {
                        SType = StructureType.CommandPoolCreateInfo,
                        QueueFamilyIndex = QueueFamilies.GraphicsFamily!.Value,
                        Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                    };

                    if (VK.CreateCommandPool(device, in poolInfo, null, out cmdPool) != Result.Success)
                        throw new Exception("failed to create command pool");

                    CommandPools[Thread.CurrentThread] = cmdPool;
                }

            }


            CommandBufferAllocateInfo allocInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = cmdPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };



            if (VK.AllocateCommandBuffers(device, in allocInfo, out var commandBuffer) != Result.Success)
                throw new Exception("failed to allocate command buffer");


            return commandBuffer;
        }






        private unsafe void CreateStagingBuffer(ulong size, out Buffer buffer, out DeviceMemory memory)
        {
            BufferCreateInfo bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive
            };

            VK.CreateBuffer(device, &bufferInfo, null, out buffer);

            VK.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements memReq);
            uint memTypeIndex = FindMemoryType(
                memReq.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            MemoryAllocateInfo allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReq.Size,
                MemoryTypeIndex = memTypeIndex
            };

            VK.AllocateMemory(device, &allocInfo, null, out memory);

            VK.BindBufferMemory(device, buffer, memory, 0);
        }






        private CommandBuffer BeginSingleTimeCommandBuffer()
        {
            var cmd = CreateCommandBufferForThisThread();

            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };

            VK.BeginCommandBuffer(cmd, &beginInfo);
            return cmd;
        }



        private void EndSingleTimeCommandBuffer(CommandBuffer cmd)
        {
            VK.EndCommandBuffer(cmd);

            FenceCreateInfo fenceInfo = new FenceCreateInfo
            {
                SType = StructureType.FenceCreateInfo
            };

            VK.CreateFence(device, &fenceInfo, null, out Fence fence);

            SubmitInfo submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd,
            };


            lock (this)
                VK.QueueSubmit(graphicsQueue, 1, &submitInfo, fence);


            VK.WaitForFences(device, 1, &fence, true, ulong.MaxValue);

            VK.DestroyFence(device, fence, null);

            lock (CommandPools)
                VK.FreeCommandBuffers(device, CommandPools[Thread.CurrentThread], 1, &cmd);
        }







        private VulkanBufferAndMemory VKCreateBuffer(uint size, BufferUsageFlags usage, bool writeable, void* initialContent = null)
        {



            BufferCreateInfo bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive
            };


            if (VK.CreateBuffer(device, &bufferInfo, null, out var buffer) != Result.Success)
                throw new Exception("Failed to create buffer.");



            MemoryPropertyFlags memoryflags;

            if (!writeable) memoryflags = MemoryPropertyFlags.DeviceLocalBit;
            else memoryflags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;



            VK.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements memReq);
            uint memoryTypeIndex = FindMemoryType(memReq.MemoryTypeBits, memoryflags);



            MemoryAllocateInfo allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReq.Size,
                MemoryTypeIndex = memoryTypeIndex
            };


            if (VK.AllocateMemory(device, &allocInfo, null, out var memory) != Result.Success)
                throw new Exception("Failed to allocate buffer memory.");         

            if (VK.BindBufferMemory(device, buffer, memory, 0) != Result.Success)
                throw new Exception("Failed to bind buffer memory.");



            void* mapped = default;
            if (writeable) VK.MapMemory(device, memory, 0, size, 0, &mapped);


            if (initialContent != null)
            {
                if (writeable) Unsafe.CopyBlockUnaligned(mapped, initialContent, size);

                else
                {

                    CreateStagingBuffer(size, out var stagingBuffer, out var stagingMemory);

                    VK.MapMemory(device, stagingMemory, 0, size, 0, &mapped);
                    Unsafe.CopyBlockUnaligned(mapped, initialContent, size);
                    VK.UnmapMemory(device, stagingMemory);

                    var cmd = BeginSingleTimeCommandBuffer();
                    BufferCopy copyRegion = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = size };
                    VK.CmdCopyBuffer(cmd, stagingBuffer, buffer, 1, &copyRegion);
                    EndSingleTimeCommandBuffer(cmd);

                    VK.DestroyBuffer(device, stagingBuffer, null);
                    VK.FreeMemory(device, stagingMemory, null);

                    mapped = default;
                }

            }

            var inst = new VulkanBufferAndMemory(buffer, memory, usage, size, mapped);

            return inst;

        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AlignUp(uint size, uint alignment) => (size + alignment - 1) & ~(alignment - 1);



        public unsafe object CreateVertexBuffer(uint length, bool writeable, void* initialContent)
        {
            return VKCreateBuffer(length, BufferUsageFlags.VertexBufferBit, writeable, initialContent);
        }

        public unsafe object CreateIndexBuffer(uint length, bool writeable, void* initialContent)
        {
            return VKCreateBuffer(length, BufferUsageFlags.IndexBufferBit, writeable, initialContent);
        }

        public unsafe object CreateUniformBuffer(uint length, bool writeable, void* initialContent)
        {
            return VKCreateBuffer(AlignUp(length, (uint)physicalDeviceProperties.Limits.MinUniformBufferOffsetAlignment), BufferUsageFlags.UniformBufferBit, writeable, initialContent);
        }

        public unsafe object CreateStorageBuffer(uint length, bool writeable, void* initialContent)
        {
            return VKCreateBuffer(AlignUp(length, (uint)physicalDeviceProperties.Limits.MinStorageBufferOffsetAlignment), BufferUsageFlags.StorageBufferBit, writeable, initialContent);
        }












        public unsafe void AdvanceActiveBufferWrite(BackendBufferAllocationReference buffer, uint idx) 
            => ((VulkanBufferAndMemory)buffer.BackendRef).CurrentConsumingWriteIndex = idx;




        public void WriteToBuffer(BackendBufferAllocationReference logicalbuffer, ReadOnlySpan<WriteRange> writes, uint idx)
        {

            var ActualVKBuffer = (VulkanBufferAndMemory)logicalbuffer.BackendRef;

            var RequiredSize = logicalbuffer.Size * (idx+1);

            var Offset = logicalbuffer.Size * idx;



            //if the currently owned buffer is big enough, write

            if (ActualVKBuffer.Size >= RequiredSize)
            {
                //write changes from cpu
                pushwrites(ref writes, ActualVKBuffer.MappedPtr);  
                return;
            }



            //otherwise, replace backendref with a buffer big enough for another snapshot and append to that

            var newBuffer = VKCreateBuffer(RequiredSize, ActualVKBuffer.UsageFlags, true, null);





            //gpu -> gpu copy buffer to new
            var singletime = BeginSingleTimeCommandBuffer();

            VK.CmdCopyBuffer(singletime, ActualVKBuffer.Buffer, newBuffer.Buffer, [new BufferCopy(0, 0, logicalbuffer.Size), new BufferCopy(0, logicalbuffer.Size, logicalbuffer.Size)]);

            EndSingleTimeCommandBuffer(singletime);




            //write changes from cpu
            pushwrites(ref writes, newBuffer.MappedPtr);




            DestroyBuffer(logicalbuffer);
            logicalbuffer.BackendRef = newBuffer;



            void pushwrites(ref ReadOnlySpan<WriteRange> writes, void* to)
            {
                for (int i = 0; i < writes.Length; i++)
                {
                    var write = writes[i];
                    Unsafe.CopyBlockUnaligned((byte*)to + Offset + write.Offset, (byte*)write.Content, write.Length);
                }
            }
        }



        private abstract class DeferredWriteBase
        {
            public DeferredWriteBase()
            {
                Handle = GCHandle<DeferredWriteBase>.Alloc(this, GCHandleType.Weak);
            }



            public readonly GCHandle<DeferredWriteBase> Handle;

            public uint CurrentConsumingWriteIndex;
        }




        private class VulkanBufferAndMemory : DeferredWriteBase
        {

            public readonly Buffer Buffer;
            public readonly uint Size;

            public readonly DeviceMemory Memory;
            public readonly BufferUsageFlags UsageFlags;


            public readonly void* MappedPtr; //null if not writeable



            public VulkanBufferAndMemory(Buffer buffer, DeviceMemory memory, BufferUsageFlags usageFlags, uint size, void* mappedPtr = default) : base()
            {
                Buffer=buffer;
                Memory=memory;
                UsageFlags=usageFlags;
                MappedPtr=mappedPtr;
                Size=size;

            }

        }





        public void DestroyBuffer(BackendBufferAllocationReference buffer)
        {
            VKDestroyBuffer((VulkanBufferAndMemory)buffer.BackendRef);
        }


        private void VKDestroyBuffer(VulkanBufferAndMemory vkbuffer)
        {
            VK.DestroyBuffer(device, vkbuffer.Buffer, null);
            VK.FreeMemory(device, vkbuffer.Memory, null);
        }





        private class VulkanDescriptorSetList(DescriptorSetLayout layout) : DeferredWriteBase()
        {
            public DescriptorSetLayout Layout = layout;

            public List<VulkanDescriptorSet> Sets = new();

        }



        private record class VulkanDescriptorSet(DescriptorSetLayout Layout, DescriptorSet Set)
        {
            public IResourceSetResource[] contents;
        }






        public object CreateResourceSet(ReadOnlySpan<ResourceSetResourceDeclaration> contentDefinition)
        {
            var details = new DescriptorSetLayoutDetails();

            for (byte i = 0; i < contentDefinition.Length; i++)
            {
                var get = contentDefinition[i];

                switch (get.ResourceType)
                {
                    case ResourceSetResourceDeclaration.ResourceSetResourceType.Texture:
                        details.ResourceType[details.ResourceCount] = (byte)DescriptorType.CombinedImageSampler;
                        break;

                    case ResourceSetResourceDeclaration.ResourceSetResourceType.UniformBuffer:
                        details.ResourceType[details.ResourceCount] = (byte)DescriptorType.UniformBufferDynamic;
                        break;

                    case ResourceSetResourceDeclaration.ResourceSetResourceType.StorageBuffer:
                        details.ResourceType[details.ResourceCount] = (byte)DescriptorType.StorageBufferDynamic;
                        break;

                    default:
                        throw new Exception();
                }

                details.ResourceArrayLength[details.ResourceCount] = uint.Max(get.ArrayLength, 1);

                details.ResourceCount++;
            }

            var inst = new VulkanDescriptorSetList(CreateOrFetchDescriptorSetLayout(details));

            AddDescriptorSet(inst);


            inst.Sets[0].contents = new IResourceSetResource[contentDefinition.Length];


            return inst;
        }



        private void AddDescriptorSet(VulkanDescriptorSetList src)
        {
            var layout = src.Layout;

            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = CreateOrFetchDescriptorPoolForThisThread(),

                DescriptorSetCount = 1,
                PSetLayouts = &layout
            };

            if (VK.AllocateDescriptorSets(device, &allocInfo, out DescriptorSet descriptorSet) != Result.Success)
                throw new Exception("failed to allocate descriptor set");

            src.Sets.Add(new(layout, descriptorSet));

        }



        public unsafe void AdvanceActiveResourceSetWrite(BackendResourceSetReference set, uint idx)
            => ((VulkanDescriptorSetList)set.BackendRef).CurrentConsumingWriteIndex = idx;



        public unsafe void WriteToResourceSet(BackendResourceSetReference set, ReadOnlySpan<ResourceSetResourceBind> contents, uint idx)
        {
            var backendref = (VulkanDescriptorSetList)set.BackendRef;


            if (idx >= backendref.Sets.Count)
            {
                AddDescriptorSet(backendref);

                var prevContents = backendref.Sets[(int)idx - 1];
                var newSet = backendref.Sets[(int)idx];

                newSet.contents = new IResourceSetResource[prevContents.contents.Length];

                Span<ResourceSetResourceBind> newcontents = stackalloc ResourceSetResourceBind[newSet.contents.Length];
                for (uint i = 0; i < newcontents.Length; i++)
                    newcontents[(int)i] = new ResourceSetResourceBind(i, ((Freeable)prevContents.contents[i]).GCHandle);

                VKWriteToDescriptorSet(newSet, newcontents);
            }



            var vkset = backendref.Sets[(int)idx];

            VKWriteToDescriptorSet(vkset, contents);

        }


        private unsafe void VKWriteToDescriptorSet(VulkanDescriptorSet vkset, ReadOnlySpan<ResourceSetResourceBind> contents)
        {


            Span<WriteDescriptorSet> writes = stackalloc WriteDescriptorSet[contents.Length];
            Span<DescriptorBufferInfo> bufferInfos = stackalloc DescriptorBufferInfo[contents.Length];
            Span<DescriptorImageInfo> imageInfos = stackalloc DescriptorImageInfo[contents.Length];


            int writeIndex = 0;

            for (int i = 0; i < contents.Length; i++)
            {
                var slot = contents[i];

                var resource = (IResourceSetResource)slot.Resource.Target;

                vkset.contents[slot.Binding] = resource;


                if (resource is BackendTextureAndSamplerReferencesPair tex)
                {
                    imageInfos[writeIndex] = new DescriptorImageInfo
                    {
                        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                        ImageView = ((VulkanTexture)tex.Texture.BackendRef).View,
                        Sampler = (Sampler)tex.Sampler.BackendRef
                    };

                    fixed (DescriptorImageInfo* p = &imageInfos[writeIndex])
                    {
                        writes[writeIndex] = new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = vkset.Set,
                            DstBinding = slot.Binding,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.CombinedImageSampler,
                            PImageInfo = p
                        };
                    }

                    writeIndex++;
                }


                else if (resource is BackendTextureAndSamplerReferencesPairsArray texArray)
                {
                    int arrayLength = texArray.Array.Length;

                    // Allocate space for DescriptorImageInfo for each element
                    var imageInfosArray = new DescriptorImageInfo[arrayLength];

                    for (int i2 = 0; i2 < arrayLength; i2++)
                    {
                        imageInfosArray[i2] = new DescriptorImageInfo
                        {
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                            ImageView = ((VulkanTexture)texArray.Array[i2].Texture.BackendRef).View,
                            Sampler = (Sampler)texArray.Array[i2].Sampler.BackendRef
                        };
                    }

                    fixed (DescriptorImageInfo* p = imageInfosArray)
                    {
                        writes[writeIndex] = new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = vkset.Set,
                            DstBinding = slot.Binding,
                            DescriptorCount = (uint)arrayLength,
                            DescriptorType = DescriptorType.CombinedImageSampler,
                            PImageInfo = p
                        };
                    }

                    if (arrayLength == 0) throw new Exception();

                    writeIndex++;
                }



                else if (resource is BackendBufferAllocationReference ubo)
                {
                    bufferInfos[writeIndex] = new DescriptorBufferInfo
                    {
                        Buffer = ((VulkanBufferAndMemory)ubo.BackendRef).Buffer,
                        Offset = 0,
                        Range = ubo.Size
                    };

                    fixed (DescriptorBufferInfo* p = &bufferInfos[writeIndex])
                    {
                        writes[writeIndex] = new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = vkset.Set,
                            DstBinding = slot.Binding,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.UniformBufferDynamic,
                            PBufferInfo = p
                        };
                    }

                    writeIndex++;
                }


                else if (resource is BackendStorageBufferAllocationReference ssbo)
                {
                    bufferInfos[writeIndex] = new DescriptorBufferInfo
                    {
                        Buffer = ((VulkanBufferAndMemory)ssbo.BackendRef).Buffer,
                        Offset = 0,
                        Range = ssbo.Size
                    };

                    fixed (DescriptorBufferInfo* p = &bufferInfos[writeIndex])
                    {
                        writes[writeIndex] = new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = vkset.Set,
                            DstBinding = slot.Binding,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType.StorageBufferDynamic,
                            PBufferInfo = p
                        };
                    }

                    writeIndex++;
                }


                else throw new NotImplementedException();
            }



            fixed (WriteDescriptorSet* pWrites = writes)
            fixed (DescriptorBufferInfo* pBuffers = bufferInfos)
            fixed (DescriptorImageInfo* pImages = imageInfos)
                VK.UpdateDescriptorSets(device, (uint)writeIndex, pWrites, 0, null);
        }







        public void DestroyResourceSet(BackendResourceSetReference set)
        {
            throw new NotImplementedException();
        }












        public object CreateTexture(Vector3<uint> Dimensions, TextureTypes type, TextureFormats format, bool FramebufferAttachmentCompatible, byte[][] texturemips = null)
        {

            var vkFormat = ConvertTextureDataFormats(format);



            ushort mipLevels = (ushort)(Math.Floor(Math.Log2(Math.Max(Math.Max(Dimensions.X, Dimensions.Y), Dimensions.Z))) + 1);

            uint LayerCount = type == TextureTypes.TextureCubeMap ? 6u : 1u;



            ImageUsageFlags usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit;


            if (FramebufferAttachmentCompatible)
            {
                if (format == TextureFormats.DepthStencil) usage |= ImageUsageFlags.DepthStencilAttachmentBit;
                else usage |= ImageUsageFlags.ColorAttachmentBit;

                usage |= ImageUsageFlags.TransferSrcBit;
            }




            ImageCreateInfo imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = vkFormat,
                Extent = new Extent3D { Width = Dimensions.X, Height = Dimensions.Y, Depth = Dimensions.Z },
                MipLevels = mipLevels,
                ArrayLayers = LayerCount,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,

                Flags = type == TextureTypes.TextureCubeMap ? ImageCreateFlags.CreateCubeCompatibleBit : 0,
            };




            if (VK.CreateImage(device, &imageInfo, null, out Image image) != Result.Success)
                throw new Exception();



            VK.GetImageMemoryRequirements(device, image, out MemoryRequirements memReq);
            uint memoryTypeIndex = FindMemoryType(memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);



            MemoryAllocateInfo allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReq.Size,
                MemoryTypeIndex = memoryTypeIndex
            };



            VK.AllocateMemory(device, &allocInfo, null, out var memory);
            VK.BindImageMemory(device, image, memory, 0);



            ImageView imageView = CreateImageView(image,
                type == TextureTypes.TextureCubeMap ? ImageViewType.TypeCube : ImageViewType.Type2D,
                vkFormat,
                format == TextureFormats.DepthStencil ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit,
                mipLevels);




            CommandBuffer cmd = BeginSingleTimeCommandBuffer();

            var currentLayout = ImageLayout.Undefined;


            Silk.NET.Vulkan.Buffer stagingBuffer = default;
            DeviceMemory stagingMemory = default;




            if (texturemips != default)
            {
                if (FramebufferAttachmentCompatible) throw new Exception("not compatible");



                TransitionImageLayoutInternal(cmd, image, vkFormat, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, 0, mipLevels, LayerCount);
                currentLayout = ImageLayout.TransferDstOptimal;



                int mipCount = texturemips.Length;

                // Calculate total size of the staging buffer
                ulong totalSize = 0;
                for (int mip = 0; mip < mipCount; mip++)
                {
                    totalSize += (ulong)texturemips[mip].Length * LayerCount;
                }

                CreateStagingBuffer(totalSize, out stagingBuffer, out stagingMemory);

                // Copy mip data into staging buffer sequentially per layer
                void* mapped;
                VK.MapMemory(device, stagingMemory, 0, totalSize, 0, &mapped);
                byte* dst = (byte*)mapped;
                ulong offset = 0;

                for (int mip = 0; mip < mipCount; mip++)
                {
                    byte[] mipData = texturemips[mip];

                    for (uint layer = 0; layer < LayerCount; layer++)
                    {
                        fixed (byte* src = mipData)
                        {
                            Unsafe.CopyBlockUnaligned(dst + offset, src, (uint)mipData.Length);
                        }
                        offset += (ulong)mipData.Length;
                    }
                }
                VK.UnmapMemory(device, stagingMemory);

                // Prepare copy regions
                Span<BufferImageCopy> regions = stackalloc BufferImageCopy[mipCount * (int)LayerCount];
                offset = 0;

                for (int mip = 0; mip < mipCount; mip++)
                {
                    uint width = Math.Max(1u, Dimensions.X >> mip);
                    uint height = Math.Max(1u, Dimensions.Y >> mip);

                    for (uint layer = 0; layer < LayerCount; layer++)
                    {
                        regions[mip * (int)LayerCount + (int)layer] = new BufferImageCopy
                        {
                            BufferOffset = offset,
                            BufferRowLength = 0,
                            BufferImageHeight = 0,

                            ImageSubresource = new ImageSubresourceLayers
                            {
                                AspectMask = format == TextureFormats.DepthStencil ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit,
                                MipLevel = (uint)mip,
                                BaseArrayLayer = layer,
                                LayerCount = 1
                            },

                            ImageOffset = default,
                            ImageExtent = new Extent3D
                            {
                                Width = width,
                                Height = height,
                                Depth = Math.Max(1u, Dimensions.Z >> mip)
                            }
                        };

                        offset += (ulong)texturemips[mip].Length;
                    }
                }

                // Copy all mips and layers to the image
                fixed (BufferImageCopy* regionsPtr = regions)
                {
                    VK.CmdCopyBufferToImage(cmd, stagingBuffer, image, ImageLayout.TransferDstOptimal, (uint)regions.Length, regionsPtr);
                }

            }




            ImageView framebufferimageView = default;

            if (FramebufferAttachmentCompatible)
            {
                framebufferimageView = CreateImageView(image,
                    type == TextureTypes.TextureCubeMap ? ImageViewType.TypeCube : ImageViewType.Type2D,
                    vkFormat,
                    format == TextureFormats.DepthStencil ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit : ImageAspectFlags.ColorBit,
                    1);


                //var newlayout = format == TextureFormats.DepthStencil ? ImageLayout.DepthStencilAttachmentOptimal : ImageLayout.ColorAttachmentOptimal;
                var newlayout = ImageLayout.ShaderReadOnlyOptimal;
                TransitionImageLayoutInternal(cmd, image, vkFormat, currentLayout, newlayout, 0, mipLevels, LayerCount);
                currentLayout = newlayout;
            }

            else
            {
                TransitionImageLayoutInternal(cmd, image, vkFormat, currentLayout, ImageLayout.ShaderReadOnlyOptimal, 0, mipLevels, LayerCount);
                currentLayout = ImageLayout.ShaderReadOnlyOptimal;
            }




            if (currentLayout == ImageLayout.Undefined) throw new Exception();


            EndSingleTimeCommandBuffer(cmd);




            if (stagingBuffer.Handle != default)
            {
                VK.DestroyBuffer(device, stagingBuffer, null);
                VK.FreeMemory(device, stagingMemory, null);
            }



            return new VulkanTexture
            {
                Image = image,
                Memory = memory,
                View = imageView,
                FramebufferCompatibleView = framebufferimageView,
                Format = vkFormat,
                CurrentLayout = currentLayout,
            };
        }





        private void TransitionImageLayoutInternal(CommandBuffer cmd, Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout, uint baseMipLevel, uint levelCount, uint arrayLayers)
        {
            ImageMemoryBarrier barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = GetAspectMask(format),
                    BaseMipLevel = baseMipLevel,
                    LevelCount = levelCount,
                    BaseArrayLayer = 0,
                    LayerCount = arrayLayers
                }
            };

            AccessFlags srcAccess = 0;
            AccessFlags dstAccess = 0;
            var srcStage = PipelineStageFlags.TopOfPipeBit;
            var dstStage = PipelineStageFlags.BottomOfPipeBit;


            switch (oldLayout)
            {
                case ImageLayout.Undefined:
                    srcAccess = 0;
                    srcStage = PipelineStageFlags.TopOfPipeBit;
                    break;

                case ImageLayout.TransferDstOptimal:
                    srcAccess = AccessFlags.TransferWriteBit;
                    srcStage = PipelineStageFlags.TransferBit;
                    break;

                case ImageLayout.TransferSrcOptimal:
                    srcAccess = AccessFlags.TransferReadBit;
                    srcStage = PipelineStageFlags.TransferBit;
                    break;

                case ImageLayout.ColorAttachmentOptimal:
                    srcAccess = AccessFlags.ColorAttachmentWriteBit;
                    srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
                    break;

                case ImageLayout.DepthStencilAttachmentOptimal:
                    srcAccess = AccessFlags.DepthStencilAttachmentWriteBit;
                    srcStage = PipelineStageFlags.LateFragmentTestsBit;
                    break;

                case ImageLayout.ShaderReadOnlyOptimal:
                    srcAccess = AccessFlags.ShaderReadBit;
                    srcStage = PipelineStageFlags.FragmentShaderBit;
                    break;

                case ImageLayout.PresentSrcKhr:
                    srcAccess = 0;
                    srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
                    break;

                default:
                    throw new Exception();
            }


            switch (newLayout)
            {
                case ImageLayout.TransferDstOptimal:
                    dstAccess = AccessFlags.TransferWriteBit;
                    dstStage = PipelineStageFlags.TransferBit;
                    break;

                case ImageLayout.TransferSrcOptimal:
                    dstAccess = AccessFlags.TransferReadBit;
                    dstStage = PipelineStageFlags.TransferBit;
                    break;

                case ImageLayout.ColorAttachmentOptimal:
                    dstAccess = AccessFlags.ColorAttachmentWriteBit;
                    dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
                    break;

                case ImageLayout.DepthStencilAttachmentOptimal:
                    dstAccess = AccessFlags.DepthStencilAttachmentWriteBit;
                    dstStage = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
                    break;

                case ImageLayout.ShaderReadOnlyOptimal:
                    dstAccess = AccessFlags.ShaderReadBit;
                    dstStage = PipelineStageFlags.FragmentShaderBit;
                    break;

                case ImageLayout.PresentSrcKhr:
                    dstAccess = 0;
                    dstStage = PipelineStageFlags.BottomOfPipeBit;
                    break;

                default: throw new Exception();
            }

            barrier.SrcAccessMask = srcAccess;
            barrier.DstAccessMask = dstAccess;


            VK.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
        }







        private ImageView CreateImageView(Image src, ImageViewType type, Format format, ImageAspectFlags aspectmask, uint mipcount)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = src,
                ViewType = type,
                Format = format,
                Components =
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
                SubresourceRange =
                {
                    AspectMask = aspectmask,
                    BaseMipLevel = 0,
                    LevelCount = mipcount,
                    BaseArrayLayer = 0,
                    LayerCount = type == ImageViewType.TypeCube ? 6u : 1u,
                }
            };

            if (VK.CreateImageView(device, in createInfo, null, out var res) != Result.Success)
                throw new Exception("failed to create image view");

            return res;
        }




        public ReadOnlySpan<byte> ReadTexturePixels(BackendTextureReference tex, uint mipLevel, Vector3<uint> offset, Vector3<uint> size)
        {

            var vkformat = ConvertTextureDataFormats(tex.TextureFormat);
            var vktex = (VulkanTexture)tex.BackendRef;


            int bpp = tex.TextureFormat switch
            {
                TextureFormats.R8_UNORM => 1,
                TextureFormats.RG8_UNORM => 2,
                TextureFormats.RGBA8_UNORM => 4,

                TextureFormats.R16_SFLOAT => 2,
                TextureFormats.RG16_SFLOAT => 4,
                TextureFormats.RGBA16_SFLOAT => 8,

                TextureFormats.DepthStencil => 4,

                _ => throw new NotImplementedException() 
            };


            ulong imageSize = (ulong)(size.X * size.Y * bpp);



            CreateStagingBuffer(
                imageSize,
                out var stagingBuffer,
                out DeviceMemory stagingMemory);




            CommandBuffer cmd = BeginSingleTimeCommandBuffer();

            var oldlayout = vktex.CurrentLayout;

            TransitionImageLayout(tex, ImageLayout.TransferSrcOptimal, cmd);


            BufferImageCopy region = new()
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = GetAspectMask(vkformat),
                    MipLevel = mipLevel,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D((int)offset.X, (int)offset.Y, (int)offset.Z),
                ImageExtent = new Extent3D(size.X, size.Y, size.Z)
            };


            VK.CmdCopyImageToBuffer(cmd, vktex.Image, ImageLayout.TransferSrcOptimal, stagingBuffer, 1, &region);

            TransitionImageLayout(tex, oldlayout, cmd);

            EndSingleTimeCommandBuffer(cmd);



            // Map memory
            void* data;
            if (VK.MapMemory(device, stagingMemory, 0, imageSize, 0, &data) != Result.Success)
                throw new Exception("Failed to map staging buffer");

            var span = new ReadOnlySpan<byte>(data, (int)imageSize);

            byte[] managedCopy = span.ToArray(); // copy required before freeing
            VK.UnmapMemory(device, stagingMemory);

            VK.DestroyBuffer(device, stagingBuffer, null);
            VK.FreeMemory(device, stagingMemory, null);

            return managedCopy;
        }





        private void TransitionImageLayout(BackendTextureReference tex, ImageLayout dest, CommandBuffer cmd)
        {
            var img = (VulkanTexture)tex.BackendRef;
            if (dest != img.CurrentLayout)
            {
                TransitionImageLayoutInternal(cmd, img.Image, img.Format, img.CurrentLayout, dest, 0, tex.MipCount, tex.TextureType == TextureTypes.TextureCubeMap ? 6u : 1u);

                img.CurrentLayout = dest;
            }
        }





        private static ImageAspectFlags GetAspectMask(Format format)
        {
            return format switch
            {
                Format.D32SfloatS8Uint or Format.D24UnormS8Uint => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
                Format.D32Sfloat or Format.D16Unorm => ImageAspectFlags.DepthBit,
                _ => ImageAspectFlags.ColorBit
            };
        }












        private static int ConvertTextureFilter(TextureFilters v)
        {
            return (int)(v switch
            {
                TextureFilters.Nearest => Filter.Nearest,
                TextureFilters.Linear => Filter.Linear,
                _ => throw new NotImplementedException(),
            });
        }
        private static int ConvertTextureWrapMode(TextureWrapModes v)
        {
            return (int)(v switch
            {
                TextureWrapModes.Repeat => SamplerAddressMode.Repeat,
                TextureWrapModes.ClampToEdge => SamplerAddressMode.ClampToEdge,
                _ => throw new NotImplementedException(),
            });
        }


        public object CreateTextureSampler(SamplerDetails details)
        {

            SamplerCreateInfo samplerInfo = new()
            {
                SType = StructureType.SamplerCreateInfo,

                MinFilter = (Filter)ConvertTextureFilter(details.MinFilter),
                MagFilter = (Filter)ConvertTextureFilter(details.MagFilter),

                AddressModeU = (SamplerAddressMode)ConvertTextureWrapMode(details.WrapMode),
                AddressModeV = (SamplerAddressMode)ConvertTextureWrapMode(details.WrapMode),
                AddressModeW = (SamplerAddressMode)ConvertTextureWrapMode(details.WrapMode),

                AnisotropyEnable = false,
                MaxAnisotropy = 1.0f,

                BorderColor = BorderColor.IntOpaqueBlack,
                UnnormalizedCoordinates = false,

                CompareEnable = details.EnableDepthComparison,

                CompareOp = details.EnableDepthComparison ? CompareOp.Always : CompareOp.LessOrEqual,

                MipmapMode = (SamplerMipmapMode)ConvertTextureFilter(details.MinFilter),
                MinLod = 0.0f,
                MaxLod = float.MaxValue,
                MipLodBias = 0.0f
            };


            if (VK.CreateSampler(device, in samplerInfo, null, out Sampler sampler) != Result.Success)
                throw new Exception("Failed to create sampler");


            return sampler;
        }

        public void DestroyTextureSampler(BackendSamplerReference texture)
        {
            VK.DestroySampler(device, (Sampler)texture.BackendRef, null);
        }






        private class VulkanTexture
        {
            public Image Image;
            public DeviceMemory Memory;
            public ImageView View;

            public ImageView FramebufferCompatibleView;  // a view with only 1 mip, if applicable

            public Format Format;

            public ImageLayout CurrentLayout;   //all mips are always transitioned to the same one layout for simplicity
        }










        public object CreateShader(ShaderSource ShaderSource)
        {
            var vertShaderModule = CreateShaderModule(ShaderSource.VertexSource.AsSpan());
            var fragShaderModule = CreateShaderModule(ShaderSource.FragmentSource.AsSpan());

            var vertShaderStageInfo = CreateStageCreateInfo(ShaderStageFlags.VertexBit, vertShaderModule);
            var fragShaderStageInfo = CreateStageCreateInfo(ShaderStageFlags.FragmentBit, fragShaderModule);


            return new VulkanShader()
            {
                VertexInfo = vertShaderStageInfo,
                FragmentInfo = fragShaderStageInfo,
                VertexModule = vertShaderModule,
                FragmentModule = fragShaderModule,
            };
        }

        private ShaderModule CreateShaderModule(ReadOnlySpan<byte> code)
        {
            ShaderModuleCreateInfo createInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
            };

            fixed (byte* codePtr = code)
            {
                createInfo.PCode = (uint*)codePtr;

                if (VK.CreateShaderModule(device, in createInfo, null, out var shaderModule) != Result.Success)
                    throw new Exception();


                return shaderModule;
            }
        }
        private PipelineShaderStageCreateInfo CreateStageCreateInfo(ShaderStageFlags stage, ShaderModule module)
        {
            return new PipelineShaderStageCreateInfo()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = stage,
                Module = module,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            };
        }


        private class VulkanShader
        {
            public PipelineShaderStageCreateInfo VertexInfo, FragmentInfo;
            public ShaderModule VertexModule, FragmentModule;
        }





        private class VulkanComputeShader
        {
            public PipelineShaderStageCreateInfo Info;
            public ShaderModule Module;
        }

        public void DestroyShader(BackendShaderReference shader)
        {
            var vkshader = (VulkanShader)shader.BackendRef;

            VK.DestroyShaderModule(device, vkshader.VertexModule, null);
            VK.DestroyShaderModule(device, vkshader.FragmentModule, null);

            SilkMarshal.Free((nint)vkshader.VertexInfo.PName);
            SilkMarshal.Free((nint)vkshader.FragmentInfo.PName);
        }




        public object CreateComputeShader(ComputeShaderSource shaderSource)
        {
            var module = CreateShaderModule(shaderSource.Source.AsSpan());

            // 3. Wrap in your struct
            return new VulkanComputeShader()
            {
                Module = module,
                Info = CreateStageCreateInfo(ShaderStageFlags.ComputeBit, module)
            };
        }

        public void DestroyComputeShader(BackendComputeShaderReference shader)
        {
            var vkshader = (VulkanComputeShader)shader.BackendRef;

            VK.DestroyShaderModule(device, vkshader.Module, null);

            SilkMarshal.Free((nint)vkshader.Info.PName);
        }












        private static Format ConvertTextureDataFormats(TextureFormats v)
        {
            return v switch
            {
                TextureFormats.R8_UNORM => Format.R8Unorm,
                TextureFormats.RG8_UNORM => Format.R8G8Unorm,
                TextureFormats.RGBA8_UNORM => Format.R8G8B8A8Unorm,

                TextureFormats.R16_SFLOAT => Format.R16Sfloat,
                TextureFormats.RG16_SFLOAT => Format.R16G16Sfloat,
                TextureFormats.RGBA16_SFLOAT => Format.R16G16B16A16Sfloat,

                TextureFormats.BC4 => Format.BC4UnormBlock,
                TextureFormats.BC5 => Format.BC5UnormBlock,
                TextureFormats.BC7 => Format.BC7UnormBlock,
                TextureFormats.BC6H_SFLOAT => Format.BC6HSfloatBlock,

                TextureFormats.DepthStencil => Format.D24UnormS8Uint,

                _ => throw new NotImplementedException(),  //may be unintentionally left out, check if format needed conversion or something for example
            };
        }







        private static BlendFactor ConvertBlendFactor(BlendingFactor v)
        {
            return v switch
            {
                BlendingFactor.Zero => BlendFactor.Zero,
                BlendingFactor.One => BlendFactor.One,
                BlendingFactor.SrcColor => BlendFactor.SrcColor,
                BlendingFactor.OneMinusSrcColor => BlendFactor.OneMinusSrcColor,
                BlendingFactor.DstColor => BlendFactor.DstColor,
                BlendingFactor.OneMinusDstColor => BlendFactor.OneMinusDstColor,
                BlendingFactor.SrcAlpha => BlendFactor.SrcAlpha,
                BlendingFactor.OneMinusSrcAlpha => BlendFactor.OneMinusSrcAlpha,
                BlendingFactor.DstAlpha => BlendFactor.DstAlpha,
                BlendingFactor.OneMinusDstAlpha => BlendFactor.OneMinusDstAlpha,
                BlendingFactor.ConstantColor => BlendFactor.ConstantColor,
                BlendingFactor.OneMinusConstantColor => BlendFactor.OneMinusConstantColor,
                BlendingFactor.ConstantAlpha => BlendFactor.ConstantAlpha,
                BlendingFactor.OneMinusConstantAlpha => BlendFactor.OneMinusConstantAlpha,
                BlendingFactor.SrcAlphaSaturate => BlendFactor.SrcAlphaSaturate,
                BlendingFactor.Src1Color => BlendFactor.Src1Color,
                BlendingFactor.OneMinusSrc1Color => BlendFactor.OneMinusSrc1Color,
                BlendingFactor.Src1Alpha => BlendFactor.Src1Alpha,
                BlendingFactor.OneMinusSrc1Alpha => BlendFactor.OneMinusSrc1Alpha,
                _ => throw new NotImplementedException(),
            };
        }

        private static BlendOp ConvertBlendOperation(BlendOperation v)
        {
            return v switch
            {
                BlendOperation.Add => BlendOp.Add,
                BlendOperation.Subtract => BlendOp.Subtract,
                BlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
                BlendOperation.Min => BlendOp.Min,
                BlendOperation.Max => BlendOp.Max,
                _ => throw new NotImplementedException(),
            };
        }




        public static Silk.NET.Vulkan.PolygonMode ConvertPolygonModes(Rendering.PolygonMode v)
        {
            return v switch
            {
                Rendering.PolygonMode.Fill => Silk.NET.Vulkan.PolygonMode.Fill,
                Rendering.PolygonMode.Line => Silk.NET.Vulkan.PolygonMode.Line,
                Rendering.PolygonMode.Point => Silk.NET.Vulkan.PolygonMode.Point,
                _ => throw new NotImplementedException(),
            };
        }

        private static StencilOp ConvertStencilOperation(StencilOperation v)
        {
            return v switch
            {
                StencilOperation.Keep => StencilOp.Keep,
                StencilOperation.Zero => StencilOp.Zero,
                StencilOperation.Replace => StencilOp.Replace,
                StencilOperation.IncrementClamp => StencilOp.IncrementAndClamp,
                StencilOperation.DecrementClamp => StencilOp.DecrementAndClamp,
                StencilOperation.Invert => StencilOp.Invert,
                StencilOperation.IncrementWrap => StencilOp.IncrementAndWrap,
                StencilOperation.DecrementWrap => StencilOp.DecrementAndWrap,
                _ => throw new NotImplementedException(),
            };
        }

        private static PrimitiveTopology ConvertPrimitiveType(PrimitiveType v)
        {
            return (v switch
            {
                PrimitiveType.Triangles => PrimitiveTopology.TriangleList,
                PrimitiveType.Lines => PrimitiveTopology.LineList,
                _ => throw new NotImplementedException(),
            });
        }




        private static CompareOp ConvertDepthOrStencilFunction(DepthOrStencilFunction v)
        {
            return v switch
            {
                DepthOrStencilFunction.Never => CompareOp.Never,
                DepthOrStencilFunction.Less => CompareOp.Less,
                DepthOrStencilFunction.Equal => CompareOp.Equal,
                DepthOrStencilFunction.LessOrEqual => CompareOp.LessOrEqual,
                DepthOrStencilFunction.Greater => CompareOp.Greater,
                DepthOrStencilFunction.NotEqual => CompareOp.NotEqual,
                DepthOrStencilFunction.GreaterOrEqual => CompareOp.GreaterOrEqual,
                DepthOrStencilFunction.Always => CompareOp.Always,
                _ => throw new NotImplementedException(),
            };
        }



        private static CullModeFlags ConvertCullMode(CullMode v)
        {
            return v switch
            {
                CullMode.Front => CullModeFlags.FrontBit,
                CullMode.Back => CullModeFlags.BackBit,
                CullMode.Disabled => CullModeFlags.None,
                _ => throw new NotImplementedException(),
            };
        }





        //drawpipeline = pipeline
        public object CreateDrawPipeline(DrawPipelineDetails details)
        {



            var FramebufferPipeline = details.FrameBufferPipelineHandle == IntPtr.Zero ? null : (BackendFrameBufferPipelineReference)GCHandle.FromIntPtr(details.FrameBufferPipelineHandle).Target;
            var Shader = (BackendShaderReference)GCHandle.FromIntPtr(details.ShaderHandle).Target;



            var retArray = new VulkanPipelineAndLayout[
                FramebufferPipeline == null ? 
                1 
                : 
                ((VulkanFramebufferPipeline)FramebufferPipeline.BackendRef).RenderPasses.Length];






            var vkshaderbundle = (VulkanShader)Shader.BackendRef;


            // --- shader stages (vertex + fragment) ---
            PipelineShaderStageCreateInfo* shaderStages =
                stackalloc PipelineShaderStageCreateInfo[] { vkshaderbundle.VertexInfo, vkshaderbundle.FragmentInfo };


            var attrCount = details.Attributes.AttributeCount;


            var attribList = new List<VertexInputAttributeDescription>();
            var bindingList = new List<VertexInputBindingDescription>();










            DynamicState* dynamic = stackalloc DynamicState[]
            {
                    DynamicState.Viewport,
                    DynamicState.Scissor
                };

            PipelineDynamicStateCreateInfo dynamicState = new()
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamic
            };

            PipelineViewportStateCreateInfo viewportState = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
                PViewports = null,
                PScissors = null
            };









            Dictionary<uint, DescriptorSetLayoutDetails> sets = new();

            foreach (var Set in Shader.Metadata.ResourceSets.Values)
            {
                var layoutDetails = new DescriptorSetLayoutDetails();

                // Uniform buffers
                foreach (var ubo in Set.Metadata.UniformBuffers.Values)
                {
                    layoutDetails.ResourceType[ubo.Binding] = (byte)DescriptorType.UniformBufferDynamic;
                    layoutDetails.ResourceArrayLength[ubo.Binding] = 1;

                    layoutDetails.ResourceCount++;
                }


                // Storage buffers
                foreach (var ssbo in Set.Metadata.StorageBuffers.Values)
                {
                    layoutDetails.ResourceType[ssbo.Binding] = (byte)DescriptorType.StorageBufferDynamic;
                    layoutDetails.ResourceArrayLength[ssbo.Binding] = 1;

                    layoutDetails.ResourceCount++;
                }



                // Textures
                foreach (var tex in Set.Metadata.Textures.Values)
                {
                    layoutDetails.ResourceType[tex.Binding] = (byte)DescriptorType.CombinedImageSampler;
                    layoutDetails.ResourceArrayLength[tex.Binding] = uint.Max(tex.Metadata.ArrayLength, 1);

                    layoutDetails.ResourceCount++;
                }


                sets[Set.Binding] = layoutDetails;

            }



            // Build final layouts (guaranteed contiguous sets)
            DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[sets.Count];

            for (byte i = 0; i < sets.Count; i++)
            {
                var get = sets[i];

                for (int v = 0; v < sets[i].ResourceCount; v++)
                {
                    var t = (DescriptorType)get.ResourceType[v];
                }

                layouts[i] = CreateOrFetchDescriptorSetLayout(sets[i]);
            }









            for (uint i = 0; i < attrCount; i++)
            {
                ref var attribute = ref details.Attributes.Attributes[(int)i];

                // One binding per attribute (mat4 still uses one binding)
                bindingList.Add(new VertexInputBindingDescription
                {
                    Binding = i,
                    Stride = attribute.Stride,
                    InputRate = attribute.Scope == VertexAttributeScope.PerInstance ? VertexInputRate.Instance : VertexInputRate.Vertex
                });

                if (attribute.FinalFormat == ShaderAttributeBufferFinalFormat.Mat4)
                {
                    // Mat4 = 4x vec4 = 4 attributes
                    for (int col = 0; col < 4; col++)
                    {
                        attribList.Add(new VertexInputAttributeDescription
                        {
                            Location = (uint)(attribute.Location + col),
                            Binding = i,
                            Format = Format.R32G32B32A32Sfloat, // always vec4
                            Offset = (uint)(attribute.Offset + col * 16) // 16 bytes per vec4
                        });
                    }
                }
                else
                {
                    attribList.Add(new VertexInputAttributeDescription
                    {
                        Location = attribute.Location,
                        Binding = i,
                        Format = GetVertexAttributeBufferFormat(attribute.SourceFormat, attribute.FinalFormat),
                        Offset = attribute.Offset
                    });
                }
            }

            // Finally flatten into arrays
            VertexInputBindingDescription* bindingDesc = stackalloc VertexInputBindingDescription[bindingList.Count];
            for (int j = 0; j < bindingList.Count; j++)
                bindingDesc[j] = bindingList[j];

            VertexInputAttributeDescription* attribDesc = stackalloc VertexInputAttributeDescription[attribList.Count];
            for (int j = 0; j < attribList.Count; j++)
                attribDesc[j] = attribList[j];

            PipelineVertexInputStateCreateInfo vertexInputInfo = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = (uint)bindingList.Count,
                PVertexBindingDescriptions = bindingDesc,
                VertexAttributeDescriptionCount = (uint)attribList.Count,
                PVertexAttributeDescriptions = attribDesc
            };


            //throw new Exception();




            // --- input assembly ---
            PipelineInputAssemblyStateCreateInfo inputAssembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = ConvertPrimitiveType(details.Rasterization.Primitive),
                PrimitiveRestartEnable = false
            };






            for (int neededPassIdx = 0; neededPassIdx < retArray.Length; neededPassIdx++)
            {


                // --- rasterizer ---
                PipelineRasterizationStateCreateInfo rasterizer = new()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = ConvertPolygonModes(details.Rasterization.PolygonMode),
                    CullMode = ConvertCullMode(details.Rasterization.CullMode),
                    FrontFace = FrontFace.CounterClockwise,
                    DepthBiasEnable = false,
                    LineWidth = 1.0f
                };



                // --- multisampling ---
                PipelineMultisampleStateCreateInfo multisampling = new()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = GetSampleCount(FramebufferPipeline == null ? FramebufferSampleCount.Sample1 : FramebufferPipeline.Details.SampleCount)
                };



                // --- depth / stencil ---
                PipelineDepthStencilStateCreateInfo depthStencil = new()
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = details.DepthStencil.DepthFunction != DepthOrStencilFunction.Never,
                    DepthWriteEnable = details.DepthStencil.DepthWrite,
                    DepthCompareOp = ConvertDepthOrStencilFunction(details.DepthStencil.DepthFunction),
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = details.DepthStencil.StencilTestEnable,
                    Front = new StencilOpState
                    {
                        FailOp = ConvertStencilOperation(details.DepthStencil.FrontStencil.FailOp),
                        PassOp = ConvertStencilOperation(details.DepthStencil.FrontStencil.PassOp),
                        DepthFailOp = ConvertStencilOperation(details.DepthStencil.FrontStencil.DepthFailOp),
                        CompareOp = ConvertDepthOrStencilFunction(details.DepthStencil.FrontStencil.CompareOp)
                    },
                    Back = new StencilOpState
                    {
                        FailOp = ConvertStencilOperation(details.DepthStencil.BackStencil.FailOp),
                        PassOp = ConvertStencilOperation(details.DepthStencil.BackStencil.PassOp),
                        DepthFailOp = ConvertStencilOperation(details.DepthStencil.BackStencil.DepthFailOp),
                        CompareOp = ConvertDepthOrStencilFunction(details.DepthStencil.BackStencil.CompareOp)
                    }
                };



                // --- color blending ---
                int colorCount = FramebufferPipeline == null ? 1 : FramebufferPipeline.Details.ColorAttachmentCount;


                PipelineColorBlendAttachmentState* colorAttachments = stackalloc PipelineColorBlendAttachmentState[Math.Max(1, colorCount)];
                for (int i = 0; i < Math.Max(1, colorCount); i++)
                {
                    colorAttachments[i] = new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = details.Blending.Enable,
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    };

                    if (details.Blending.Enable)
                    {
                        colorAttachments[i].SrcColorBlendFactor = ConvertBlendFactor(details.Blending.SrcColor);
                        colorAttachments[i].DstColorBlendFactor = ConvertBlendFactor(details.Blending.DstColor);
                        colorAttachments[i].ColorBlendOp = ConvertBlendOperation(details.Blending.ColorOp);

                        colorAttachments[i].SrcAlphaBlendFactor = ConvertBlendFactor(details.Blending.SrcAlpha);
                        colorAttachments[i].DstAlphaBlendFactor = ConvertBlendFactor(details.Blending.DstAlpha);
                        colorAttachments[i].AlphaBlendOp = ConvertBlendOperation(details.Blending.AlphaOp);
                    }
                }


                PipelineColorBlendStateCreateInfo colorBlending = new()
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    LogicOp = LogicOp.Copy,
                    AttachmentCount = (uint)Math.Max(1, colorCount),
                    PAttachments = colorAttachments
                };



                PipelineLayoutCreateInfo pipelineLayoutInfo = new()
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)sets.Count,
                    PSetLayouts = layouts
                };



                if (VK.CreatePipelineLayout(device, in pipelineLayoutInfo, null, out PipelineLayout layout) != Result.Success)
                    throw new Exception("Failed to create pipeline layout");



                // --- construct final pipeline create info ---
                GraphicsPipelineCreateInfo pipelineInfo = new()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PDepthStencilState = &depthStencil,
                    PColorBlendState = &colorBlending,
                    PDynamicState = &dynamicState,
                    Layout = layout,
                    RenderPass = FramebufferPipeline == null ? SwapChainRenderPass : ((VulkanFramebufferPipeline)(FramebufferPipeline.BackendRef)).RenderPasses[neededPassIdx].Pass,
                    Subpass = 0
                };

                // create pipeline
                if (VK.CreateGraphicsPipelines(device, default, 1, in pipelineInfo, null, out Pipeline pipeline) != Result.Success)
                    throw new Exception("Failed to create graphics pipeline");



                retArray[neededPassIdx] = new VulkanPipelineAndLayout(pipeline, layout);

            }



            return retArray;

        
        }




        private record class VulkanPipelineAndLayout(Pipeline Pipeline, PipelineLayout Layout);




        private static SampleCountFlags GetSampleCount(FramebufferSampleCount count)
            => count switch
            {
                FramebufferSampleCount.Sample1 => SampleCountFlags.Count1Bit,
                FramebufferSampleCount.Sample2 => SampleCountFlags.Count2Bit,
                FramebufferSampleCount.Sample4 => SampleCountFlags.Count4Bit,
                FramebufferSampleCount.Sample8 => SampleCountFlags.Count8Bit,
                FramebufferSampleCount.Sample16 => SampleCountFlags.Count16Bit,
                _ => throw new NotImplementedException(),
            };





        private static Format GetVertexAttributeBufferFormat(VertexAttributeBufferComponentFormat src, ShaderAttributeBufferFinalFormat final)
        {
            return (src, final) switch
            {

                (VertexAttributeBufferComponentFormat.Float, ShaderAttributeBufferFinalFormat.Float) => Format.R32Sfloat,
                (VertexAttributeBufferComponentFormat.Float, ShaderAttributeBufferFinalFormat.Vec2) => Format.R32G32Sfloat,
                (VertexAttributeBufferComponentFormat.Float, ShaderAttributeBufferFinalFormat.Vec3) => Format.R32G32B32Sfloat,
                (VertexAttributeBufferComponentFormat.Float, ShaderAttributeBufferFinalFormat.Vec4) => Format.R32G32B32A32Sfloat,


                (VertexAttributeBufferComponentFormat.Half, ShaderAttributeBufferFinalFormat.Float) => Format.R16Sfloat,
                (VertexAttributeBufferComponentFormat.Half, ShaderAttributeBufferFinalFormat.Vec2) => Format.R16G16Sfloat,
                (VertexAttributeBufferComponentFormat.Half, ShaderAttributeBufferFinalFormat.Vec3) => Format.R16G16B16Sfloat,
                (VertexAttributeBufferComponentFormat.Half, ShaderAttributeBufferFinalFormat.Vec4) => Format.R16G16B16A16Sfloat,


                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.Float) => Format.R8Unorm,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.Vec2) => Format.R8G8Unorm,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.Vec3) => Format.R8G8B8Unorm,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.Vec4) => Format.R8G8B8A8Unorm,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.UInt) => Format.R8Uint,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.UVec2) => Format.R8G8Uint,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.UVec3) => Format.R8G8B8Uint,
                (VertexAttributeBufferComponentFormat.Byte, ShaderAttributeBufferFinalFormat.UVec4) => Format.R8G8B8A8Uint,

                _ => throw new NotImplementedException()
            };
        }




        private struct DescriptorSetLayoutDetails
        {
            public ushort ResourceCount;
            public fixed byte ResourceType[MaxResourceSetResources];
            public fixed uint ResourceArrayLength[MaxResourceSetResources];
        }





        private readonly Dictionary<DescriptorSetLayoutDetails, DescriptorSetLayout> DescriptorSetLayoutCache = new();

        private DescriptorSetLayout CreateOrFetchDescriptorSetLayout(DescriptorSetLayoutDetails details)
        {
            lock (DescriptorSetLayoutCache)
            {
                if (DescriptorSetLayoutCache.TryGetValue(details, out var entry))
                    return entry;


                Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[details.ResourceCount];

                for (int i = 0; i < details.ResourceCount; i++)
                {
                    bindings[i] = new DescriptorSetLayoutBinding
                    {
                        Binding = (uint)i,
                        DescriptorCount = details.ResourceArrayLength[i],
                        DescriptorType = (DescriptorType)details.ResourceType[i],
                        PImmutableSamplers = null,
                        StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
                    };

                }

                DescriptorSetLayout layout;

                fixed (DescriptorSetLayoutBinding* pBindings = bindings)
                {
                    DescriptorSetLayoutCreateInfo layoutInfo = new()
                    {
                        SType = StructureType.DescriptorSetLayoutCreateInfo,
                        BindingCount = (uint)bindings.Length,
                        PBindings = pBindings
                    };
                    if (VK.CreateDescriptorSetLayout(device, &layoutInfo, null, out layout) != Result.Success)
                        throw new Exception("failed to create descriptor set layout");
                }

                DescriptorSetLayoutCache[details] = layout;

                return layout;
            }
        }









        private record class VulkanIndividualFramebufferDetails(

            Framebuffer Framebuffer,
            ImageView[] Views

        );






        public object CreateFrameBufferObject(ReadOnlySpan<BackendTextureReference> colorTargets, BackendTextureReference depthStencilTarget, BackendFrameBufferPipelineReference pipeline, Vector2<uint> dimensions)
        {
            var rp = (VulkanFramebufferPipeline)pipeline.BackendRef;



            VulkanIndividualFramebufferDetails[] vkFramebuffers = new VulkanIndividualFramebufferDetails[rp.RenderPasses.Length];


            for (int i = 0; i < rp.RenderPasses.Length; i++)
            {
                ref var pass = ref rp.RenderPasses[i];

                var views = new ImageView[pass.AttachmentCount];
                int idx = 0;

                // Add ONLY color attachments actually used by this render pass
                for (int c = 0; c < pass.ColorAttachmentCount; c++)
                    views[idx++] =
                        ((VulkanTexture)colorTargets[c].BackendRef).FramebufferCompatibleView;

                if (pass.HasDepthStencil)
                    views[idx++] =
                        ((VulkanTexture)depthStencilTarget.BackendRef).FramebufferCompatibleView;

                fixed (ImageView* pViews = views)
                {
                    FramebufferCreateInfo fbInfo = new()
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = pass.Pass,
                        AttachmentCount = (uint)views.Length,
                        PAttachments = pViews,
                        Width = dimensions.X,
                        Height = dimensions.Y,
                        Layers = 1
                    };

                    VK.CreateFramebuffer(device, &fbInfo, null, out Framebuffer fb);
                    vkFramebuffers[i] = new VulkanIndividualFramebufferDetails(fb, views);
                }
            }

            return vkFramebuffers;


        }







        private enum AttachmentUsageState
        {
            Unused,
            Written,
            ReadOnly
        }






        //a FrameBufferPipeline in this implementation gets converted to as few render passes as possible in favor of subpasses where possible.

        public object CreateFrameBufferPipeline(FrameBufferPipelineDetails details)
        {
            // ---------------------------------------------------------------------
            // Helpers
            // ---------------------------------------------------------------------

            static bool HasRead(byte a)
                => ((FrameBufferPipelineAttachmentAccessFlags)a &
                    FrameBufferPipelineAttachmentAccessFlags.Read) != 0;

            static bool HasWrite(byte a)
                => ((FrameBufferPipelineAttachmentAccessFlags)a &
                    FrameBufferPipelineAttachmentAccessFlags.Write) != 0;

            static bool CanAppendStage(
                FrameBufferPipelineStage next,
                AttachmentUsageState depthState,
                AttachmentUsageState stencilState,
                AttachmentUsageState[] colorStates,
                out AttachmentUsageState newDepth,
                out AttachmentUsageState newStencil,
                out AttachmentUsageState[] newColors)
            {
                newDepth   = depthState;
                newStencil = stencilState;
                newColors  = (AttachmentUsageState[])colorStates.Clone();

                // Depth
                if (HasWrite(next.SpecifiedDepthAccess) &&
                    newDepth == AttachmentUsageState.ReadOnly)
                    return false;

                if (HasRead(next.SpecifiedDepthAccess) &&
                    newDepth == AttachmentUsageState.Written)
                    newDepth = AttachmentUsageState.ReadOnly;
                else if (HasWrite(next.SpecifiedDepthAccess))
                    newDepth = AttachmentUsageState.Written;

                // Stencil
                if (HasWrite(next.SpecifiedStencilAccess) &&
                    newStencil == AttachmentUsageState.ReadOnly)
                    return false;

                if (HasRead(next.SpecifiedStencilAccess) &&
                    newStencil == AttachmentUsageState.Written)
                    newStencil = AttachmentUsageState.ReadOnly;
                else if (HasWrite(next.SpecifiedStencilAccess))
                    newStencil = AttachmentUsageState.Written;

                // Color
                for (int i = 0; i < newColors.Length; i++)
                {
                    var access = next.SpecifiedColorAccesses[i];

                    if (HasWrite(access) && newColors[i] == AttachmentUsageState.ReadOnly)
                        return false;

                    if (HasRead(access) && newColors[i] == AttachmentUsageState.Written)
                        newColors[i] = AttachmentUsageState.ReadOnly;
                    else if (HasWrite(access))
                        newColors[i] = AttachmentUsageState.Written;
                }

                return true;
            }

            // ---------------------------------------------------------------------
            // 1) Partition stages into render passes
            // ---------------------------------------------------------------------

            List<List<int>> renderPasses = new();
            List<int> currentRP = new() { 0 };

            AttachmentUsageState depthState = AttachmentUsageState.Unused;
            AttachmentUsageState stencilState = AttachmentUsageState.Unused;
            AttachmentUsageState[] colorStates =
                new AttachmentUsageState[details.ColorAttachmentCount];

            for (int i = 1; i < details.StageCount; i++)
            {
                if (CanAppendStage(
                    details.Stages[i],
                    depthState,
                    stencilState,
                    colorStates,
                    out var nd, out var ns, out var nc))
                {
                    depthState   = nd;
                    stencilState = ns;
                    colorStates  = nc;
                    currentRP.Add(i);
                }
                else
                {
                    renderPasses.Add(currentRP);
                    currentRP = new() { i };

                    depthState   = AttachmentUsageState.Unused;
                    stencilState = AttachmentUsageState.Unused;
                    colorStates =
                        new AttachmentUsageState[details.ColorAttachmentCount];
                }
            }

            renderPasses.Add(currentRP);

            // ---------------------------------------------------------------------
            // 2) Build Vulkan render passes
            // ---------------------------------------------------------------------

            var pipeline = new VulkanFramebufferPipeline
            {
                Stages       = new VulkanFramebufferPipeline.Stage[details.StageCount],
                RenderPasses = new VulkanFramebufferPipeline.RenderPassDetails[renderPasses.Count]
            };

            byte rpIndex = 0;

            foreach (var rpStages in renderPasses)
            {
                bool[] colorUsed = new bool[details.ColorAttachmentCount];
                bool depthUsed = false;
                bool clearDepth = false;
                bool clearStencil = false;
                bool[] clearColors = new bool[details.ColorAttachmentCount];

                // Determine which attachments are used and which need clears
                foreach (int s in rpStages)
                {
                    var stage = details.Stages[s];

                    for (int i = 0; i < colorUsed.Length; i++)
                    {
                        bool used = stage.SpecifiedColorAccesses[i] != 0;
                        colorUsed[i] |= used;
                        clearColors[i] |= used && (stage.SpecifiedColorClears & (1 << i)) != 0;
                    }

                    depthUsed |= stage.SpecifiedDepth;
                    clearDepth |= stage.SpecifiedDepth && stage.SpecifiedClearDepth;

                    clearStencil |= stage.SpecifiedStencil && stage.SpecifiedClearStencil;
                }

                int colorAttachmentCount = colorUsed.Count(b => b);
                int totalAttachmentCount = colorAttachmentCount + (depthUsed ? 1 : 0);

                Span<AttachmentDescription> attachments = stackalloc AttachmentDescription[totalAttachmentCount];
                int[] colorIndexMap = new int[details.ColorAttachmentCount];
                int attachmentIdx = 0;

                // Color attachments
                for (int i = 0; i < colorUsed.Length; i++)
                {
                    if (!colorUsed[i])
                    {
                        colorIndexMap[i] = -1;
                        continue;
                    }

                    colorIndexMap[i] = attachmentIdx;

                    attachments[attachmentIdx++] = new AttachmentDescription
                    {
                        Format = ConvertTextureDataFormats((TextureFormats)details.ColorFormats[i]),
                        Samples = GetSampleCount(details.SampleCount),
                        LoadOp = clearColors[i] ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                        InitialLayout = ImageLayout.ShaderReadOnlyOptimal,
                        FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
                        StencilLoadOp = AttachmentLoadOp.DontCare,
                        StencilStoreOp = AttachmentStoreOp.DontCare
                    };
                }

                // Depth/stencil attachment
                AttachmentReference depthRef = default;
                if (depthUsed)
                {
                    attachments[attachmentIdx] = new AttachmentDescription
                    {
                        Format = Format.D24UnormS8Uint,
                        Samples = GetSampleCount(details.SampleCount),
                        LoadOp = clearDepth ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                        StencilLoadOp = clearStencil ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                        StencilStoreOp = AttachmentStoreOp.Store,
                        InitialLayout = ImageLayout.ShaderReadOnlyOptimal,
                        FinalLayout = ImageLayout.ShaderReadOnlyOptimal
                    };

                    depthRef = new AttachmentReference
                    {
                        Attachment = (uint)attachmentIdx,
                        Layout = ImageLayout.DepthStencilAttachmentOptimal
                    };

                    attachmentIdx++;
                }

                // Subpasses
                var subpasses = stackalloc SubpassDescription[rpStages.Count];
                var dependencies = stackalloc SubpassDependency[rpStages.Count];
                var subpassColorRefs = new AttachmentReference[rpStages.Count][];

                for (int sp = 0; sp < rpStages.Count; sp++)
                {
                    int stageIdx = rpStages[sp];
                    pipeline.Stages[stageIdx] = new VulkanFramebufferPipeline.Stage(rpIndex, (byte)sp);

                    var refs = new List<AttachmentReference>();
                    for (int i = 0; i < colorIndexMap.Length; i++)
                    {
                        if (colorIndexMap[i] >= 0 &&
                            details.Stages[stageIdx].SpecifiedColorAccesses[i] != 0)
                        {
                            refs.Add(new AttachmentReference
                            {
                                Attachment = (uint)colorIndexMap[i],
                                Layout = ImageLayout.ColorAttachmentOptimal
                            });
                        }
                    }

                    subpassColorRefs[sp] = refs.ToArray();

                    fixed (AttachmentReference* refPtr = subpassColorRefs[sp])
                    {
                        subpasses[sp] = new SubpassDescription
                        {
                            PipelineBindPoint = PipelineBindPoint.Graphics,
                            ColorAttachmentCount = (uint)subpassColorRefs[sp].Length,
                            PColorAttachments = refPtr,
                            PDepthStencilAttachment = depthUsed ? &depthRef : null
                        };
                    }

                    dependencies[sp] = new SubpassDependency
                    {
                        SrcSubpass = sp == 0 ? Vk.SubpassExternal : (uint)(sp - 1),
                        DstSubpass = (uint)sp,
                        SrcStageMask =
                            PipelineStageFlags.ColorAttachmentOutputBit |
                            PipelineStageFlags.EarlyFragmentTestsBit,
                        DstStageMask =
                            PipelineStageFlags.ColorAttachmentOutputBit |
                            PipelineStageFlags.EarlyFragmentTestsBit,
                        SrcAccessMask = 0,
                        DstAccessMask =
                            AccessFlags.ColorAttachmentWriteBit |
                            AccessFlags.DepthStencilAttachmentWriteBit
                    };
                }

                fixed (AttachmentDescription* attachmentsPtr = attachments)
                {
                    RenderPassCreateInfo rpInfo = new()
                    {
                        SType = StructureType.RenderPassCreateInfo,
                        AttachmentCount = (uint)totalAttachmentCount,
                        PAttachments = attachmentsPtr,
                        SubpassCount = (uint)rpStages.Count,
                        PSubpasses = subpasses,
                        DependencyCount = (uint)rpStages.Count,
                        PDependencies = dependencies
                    };

                    RenderPass pass;
                    VK.CreateRenderPass(device, &rpInfo, null, &pass);

                    pipeline.RenderPasses[rpIndex++] =
                        new VulkanFramebufferPipeline.RenderPassDetails(
                            pass,
                            (byte)totalAttachmentCount,
                            (byte)colorAttachmentCount,
                            depthUsed);
                }
            }

            return pipeline;
        }





        //this is a generated list of render passes and sometimes subpasses.
        //some stage transitions would be illegal as subpass transitions, so they need to be split into render passes
        private class VulkanFramebufferPipeline
        {
            public readonly record struct Stage(byte RenderPass, byte SubPass);

            public readonly record struct RenderPassDetails(RenderPass Pass, byte AttachmentCount, byte ColorAttachmentCount, bool HasDepthStencil);

            public Stage[] Stages;

            public RenderPassDetails[] RenderPasses;
        }



        public void BeginFrameBufferPipeline(
            LogicalFrameBuffer fbo,
            BackendFrameBufferPipelineReference pipelineRef)
        {
            var p = (VulkanFramebufferPipeline)pipelineRef.BackendRef;
            var s = p.Stages[0];
            ref var pass = ref p.RenderPasses[s.RenderPass];


            StartRenderPass(
                fbo.Dimensions,
                ((VulkanIndividualFramebufferDetails[])fbo.GetFramebuffer(pipelineRef).BackendRef)[s.RenderPass].Framebuffer,
                pass);
        }


        public void AdvanceFrameBufferPipeline(
            LogicalFrameBuffer fbo,
            BackendFrameBufferPipelineReference pipelineRef,
            byte stageIndex)
        {
            var p = (VulkanFramebufferPipeline)pipelineRef.BackendRef;
            var curr = p.Stages[stageIndex];
            var prev = p.Stages[stageIndex - 1];

            if (curr.RenderPass == prev.RenderPass)
            {
                VK.CmdNextSubpass(RenderThreadCommandBuffer, SubpassContents.Inline);
                return;
            }

            VK.CmdEndRenderPass(RenderThreadCommandBuffer);

            ref var pass = ref p.RenderPasses[curr.RenderPass];


            StartRenderPass(
                fbo.Dimensions,
                ((VulkanIndividualFramebufferDetails[])
                    fbo.GetFramebuffer(pipelineRef).BackendRef)[curr.RenderPass].Framebuffer,
                pass);
        }


        public void EndFrameBufferPipeline(LogicalFrameBuffer fbo)
        {
            VK.CmdEndRenderPass(RenderThreadCommandBuffer);
        }


        private void StartRenderPass(
            Vector2<uint> dimensions,
            Framebuffer framebuffer,
            VulkanFramebufferPipeline.RenderPassDetails pass)
        {
            Span<ClearValue> clears = stackalloc ClearValue[pass.AttachmentCount];
            int idx = 0;

            for (int i = 0; i < pass.ColorAttachmentCount; i++)
                clears[idx++] = new ClearValue
                {
                    Color = new ClearColorValue(0f, 0f, 0f, 1f)
                };

            if (pass.HasDepthStencil)
                clears[idx++] = new ClearValue
                {
                    DepthStencil = new ClearDepthStencilValue(1f, 0)
                };

            fixed (ClearValue* p = clears)
            {
                RenderPassBeginInfo begin = new()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = pass.Pass,
                    Framebuffer = framebuffer,
                    RenderArea = new Rect2D
                    {
                        Offset = default,
                        Extent = new Extent2D
                        {
                            Width = dimensions.X,
                            Height = dimensions.Y
                        }
                    },
                    ClearValueCount = pass.AttachmentCount,
                    PClearValues = p
                };

                VK.CmdBeginRenderPass(
                    RenderThreadCommandBuffer,
                    &begin,
                    SubpassContents.Inline);
            }
        }



        private RenderPass SwapChainRenderPass;



        public void StartDrawToScreen()
        {
            StartRenderPass(RenderingBackend.CurrentSwapchainDetails.Size, swapChainFramebuffers[CurrentSwapChainImageIndex], new(SwapChainRenderPass, 1, 1, false));
        }


        public void EndDrawToScreen()
        {
            VK.CmdEndRenderPass(RenderThreadCommandBuffer);
        }












        public void Draw(

            ReadOnlySpan<VertexAttributeDefinitionPlusBufferStruct> AttributeBuffers,
            ReadOnlySpan<GCHandle<BackendResourceSetReference>> ResourceSets,
            BackendDrawPipelineReference pipeline,
            BackendIndexBufferAllocationReference indexBuffer,
            uint indexBufferOffset,
            IndexingDetails indexing)

        {




            var vkPipelineAndLayout = ((VulkanPipelineAndLayout[])pipeline.BackendRef)[ActiveFrameBufferPipelineStage];

            var vkpipeline = vkPipelineAndLayout.Pipeline;
            var vkLayout = vkPipelineAndLayout.Layout;



            // Bind graphics pipeline
            VK.CmdBindPipeline(RenderThreadCommandBuffer, PipelineBindPoint.Graphics, vkpipeline);



            // Bind vertex buffers
            var bufferHandles = stackalloc Buffer[AttributeBuffers.Length];
            var bufferOffsets = stackalloc ulong[AttributeBuffers.Length];


            for (int i = 0; i < AttributeBuffers.Length; i++)
            {
                var bind = AttributeBuffers[i];

                var bufferResource = bind.Buffer.Target;
                var backendref = ((VulkanBufferAndMemory)bufferResource.BackendRef);

                bufferHandles[i] = backendref.Buffer;
                bufferOffsets[i] = backendref.CurrentConsumingWriteIndex * bufferResource.Size;

            }



            VK.CmdBindVertexBuffers(RenderThreadCommandBuffer, 0, (uint)AttributeBuffers.Length, bufferHandles, bufferOffsets);



            // Bind index buffer if present
            if (indexBuffer != null)
            {
                var indexBufferBackendRef = (VulkanBufferAndMemory)indexBuffer.BackendRef;

                VK.CmdBindIndexBuffer(RenderThreadCommandBuffer, indexBufferBackendRef.Buffer, indexBufferOffset + (indexBufferBackendRef.CurrentConsumingWriteIndex * indexBuffer.Size), IndexType.Uint32);
            }






            // Bind descriptor sets with dynamic offsets if necessary
            for (int i = 0; i < ResourceSets.Length; i++)
            {
                var get = ResourceSets[i].Target;

                var set = (VulkanDescriptorSetList)get.BackendRef;


                var contentsspan = get.GetContents();

                int dynamicOffsetCount = 0;
                Span<uint> dynamicOffsets = stackalloc uint[contentsspan.Length];


                for (int r = 0; r < contentsspan.Length; r++)
                {
                    var content = contentsspan[r];

                    if (content is BackendUniformBufferAllocationReference ubo)
                    {
                        dynamicOffsets[dynamicOffsetCount++] = ((VulkanBufferAndMemory)ubo.BackendRef).CurrentConsumingWriteIndex * AlignUp(ubo.Size, (uint)physicalDeviceProperties.Limits.MinUniformBufferOffsetAlignment);
                    }

                    else if (content is BackendStorageBufferAllocationReference ssbo)
                    {
                        dynamicOffsets[dynamicOffsetCount++] = ((VulkanBufferAndMemory)ssbo.BackendRef).CurrentConsumingWriteIndex * AlignUp(ssbo.Size, (uint)physicalDeviceProperties.Limits.MinStorageBufferOffsetAlignment);
                    }


                }


                var s = set.Sets[(int)set.CurrentConsumingWriteIndex].Set;

                // bind descriptor set with dynamic offsets
                fixed (uint* pOffsets = dynamicOffsets)
                {
                    VK.CmdBindDescriptorSets(
                        RenderThreadCommandBuffer,
                        PipelineBindPoint.Graphics,
                        vkLayout,
                        (uint)i,
                        1,
                        &s,
                        (uint)dynamicOffsetCount,
                        pOffsets
                    );
                }
            }





            // Set viewport and scissor
            var dims = ActiveFrameBuffer == null ? CurrentSwapchainDetails.Size : ActiveFrameBuffer.Dimensions;

            Viewport viewport = new()
            {
                X = 0,
                Y = 0,
                Width = dims.X,
                Height = dims.Y,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };


            VK.CmdSetViewport(RenderThreadCommandBuffer, 0, 1, &viewport);


            Rect2D scissor = new()
            {
                Offset = new Offset2D { X = (int)ScissorOffset.X, Y = (int)ScissorOffset.Y },
                Extent = new Extent2D { Width = ScissorSize.X, Height = ScissorSize.Y }
            };

            VK.CmdSetScissor(RenderThreadCommandBuffer, 0, 1, &scissor);




            // Issue the draw
            if (indexBuffer != null)
            {
                VK.CmdDrawIndexed(
                    RenderThreadCommandBuffer,
                    indexing.End - indexing.Start, // index count
                    indexing.InstanceCount,
                    indexing.Start,               // first index
                    (int)indexing.BaseVertex,    // vertex offset
                    0                            // first instance
                );
            }
            else
            {
                VK.CmdDraw(
                    RenderThreadCommandBuffer,
                    indexing.End - indexing.Start, // vertex count
                    indexing.InstanceCount,
                    indexing.Start,               // first vertex
                    0
                );
            }
        }














        private static Vector2<uint> ScissorOffset, ScissorSize;


        public void SetScissor(Vector2<uint> offset, Vector2<uint> size)
        { 
            ScissorOffset = offset;
            ScissorSize = size;
        }





        public void Destroy()
        {
            foreach (var p in CommandPools) 
                VK.DestroyCommandPool(device, p.Value, null);

            foreach (var p in DescriptorPools)
                VK.DestroyDescriptorPool(device, p.Value, null);

            VK.DestroyRenderPass(device, SwapChainRenderPass, null);

            VK.DestroyDevice(device, null);

            VK.DestroyInstance(instance, null);

        }




        public void GenerateMipmaps(BackendTextureReference texture)
        {
            throw new NotImplementedException();
        }


        public void DestroyTexture(BackendTextureReference texture)
        {
            var tex = ((VulkanTexture)texture.BackendRef);

            VK.DestroyImageView(device, tex.View, null);
            VK.DestroyImage(device, tex.Image, null);
            VK.FreeMemory(device, tex.Memory, null);

            if (tex.FramebufferCompatibleView.Handle != 0)
                VK.DestroyImageView(device, tex.FramebufferCompatibleView, null);
        }


        public void DestroyFrameBufferObject(BackendFrameBufferObjectReference buffer)
        {
            var array = (VulkanIndividualFramebufferDetails[])buffer.BackendRef;
            for (int i = 0; i<array.Length; i++)
            {
                VK.DestroyFramebuffer(device, array[i].Framebuffer, null);
            }
        }



        public void DispatchComputeShader(BackendComputeShaderReference shader, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            throw new NotImplementedException();
        }

        public void WaitForAllComputeShaders()
        {
            throw new NotImplementedException();
        }


        public void DestroyDrawPipeline(BackendDrawPipelineReference pipeline)
        {
            //layouts are derived from a cache and arent owned by the pipeline

            var pipelines = (VulkanPipelineAndLayout[])pipeline.BackendRef;
            for (int i = 0; i<pipelines.Length; i++)
            {

                VK.DestroyPipeline(device, pipelines[i].Pipeline, null);
            }
        }



        public void DestroyFrameBufferPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            var passes = (VulkanFramebufferPipeline)pipeline.BackendRef;

            for (int i = 0; i < passes.RenderPasses.Length; i++)
                VK.DestroyRenderPass(device, passes.RenderPasses[i].Pass, null);
        }



        public void ClearFramebufferDepthStencil(LogicalFrameBuffer framebuffer, byte CubemapFaceIfCubemap = 0)
        {
            throw new NotImplementedException();
        }

        public void ClearFramebufferColorAttachment(LogicalFrameBuffer framebuffer, Vector4 color, byte idx = 0, byte CubemapFaceIfCubemap = 0)
        {
            throw new NotImplementedException();
        }







        public void WriteTexturePixels(BackendTextureReference tex, uint level, Vector3<uint> offset, Vector3<uint> size, ReadOnlySpan<byte> content)
        {
            throw new NotImplementedException();
        }



    }






}

#endif