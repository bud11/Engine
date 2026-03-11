

#if FALSE



namespace Engine.Core;
using Engine.Core;
using SDL3;
using System;
using System.Numerics;

public static partial class RenderingBackend
{


    private unsafe class SDL3Backend : IRenderingBackend
    {
        public void AdvanceActiveBufferWrite(BackendBufferAllocationReference buffer, uint idx)
        {
            throw new NotImplementedException();
        }

        public void AdvanceActiveResourceSetWrite(BackendResourceSetReference set, uint idx)
        {
            throw new NotImplementedException();
        }

        public void AdvanceFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline, byte stageIndex)
        {
            throw new NotImplementedException();
        }

        public void BeginFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline)
        {
            throw new NotImplementedException();
        }

        public void ClearFramebufferColorAttachment(BackendFrameBufferObjectReference framebuffer, Vector4 color, byte idx = 0, byte CubemapFaceIfCubemap = 0)
        {
            SDL.clear
        }

        public void ClearFramebufferDepthStencil(BackendFrameBufferObjectReference framebuffer, byte CubemapFaceIfCubemap = 0)
        {
            throw new NotImplementedException();
        }

        public SwapchainDetails ConfigureSwapchain(EngineMath.Vector2<uint> Size, bool UseHDR)
        {
            throw new NotImplementedException();
        }

        public object CreateComputeShader(ComputeShaderSource ShaderSource)
        {
            throw new NotImplementedException();
        }

        public object CreateDrawPipeline(DrawPipelineDetails details)
        {
            return SDL.CreateGPUGraphicsPipeline(0, new()
            {
                DepthStencilState = new()
                {
                    //etc
                },
            });
        }


        public unsafe object CreateIndexBuffer(uint length, bool writeable, void* initialContent)
        {
            throw new NotImplementedException();
        }



        public object CreateResourceSet(ReadOnlySpan<ResourceSetResourceDeclaration> definition)
        {
            throw new NotImplementedException();
        }

        public object CreateShader(ShaderSource ShaderSource)
        {
            fixed (byte* vert = ShaderSource.VertexSource.AsSpan())
            fixed (byte* frag = ShaderSource.FragmentSource.AsSpan())
            {
                var vertShader = SDL.CreateGPUShader(0, new()
                {
                    Code = (nint)vert,
                    CodeSize = (nuint)ShaderSource.VertexSource.Length,
                    Stage = SDL.GPUShaderStage.Vertex,
                    Entrypoint = "main",
                    Format = RenderingBackend.GetRequiredShaderFormatForBackend(),
                    NumSamplers = (uint)ShaderSource.Metadata.ResourceSets.Values.Select(x => x.Metadata.Textures.Count).Aggregate((a, b) => a + b),

                });


                return vertShader;
            }
        }



        public unsafe object CreateStorageBuffer(uint length, bool writeable, void* initialContent)
        {
            throw new NotImplementedException();
        }

        public object CreateTexture(EngineMath.Vector3<uint> Dimensions, TextureTypes type, TextureFormats format, bool FramebufferAttachmentCompatible, byte[][] texturemips = null)
        {
            SDL.CreateGPUTexture(0, new() { Type = type, Format = format, Height = Dimensions.X, Width = Dimensions.Y, LayerCountOrDepth = Dimensions.Z, NumLevels = texturemips.Length, Usage = SDL.GPUTextureUsageFlags.Sampler });
        }

        public object CreateTextureSampler(SamplerDetails details)
        {
            return SDL.CreateGPUSampler(0, new()
            {
                AddressModeU = SDL.GPUSamplerAddressMode.Repeat,
                AddressModeV = SDL.GPUSamplerAddressMode.Repeat,
                AddressModeW = SDL.GPUSamplerAddressMode.Repeat,

                CompareOp = details.EnableDepthComparison ? SDL.GPUCompareOp.Never : SDL.GPUCompareOp.LessOrEqual,

                MipmapMode = details.MipmapFilter,

                MagFilter = details.MagFilter,
                MinFilter = details.MinFilter
            });
        }



        public unsafe object CreateUniformBuffer(uint length, bool writeable, void* initialContent)
        {
            return SDL.CreateGPUBuffer(0, new()
            {
                Size = length,
                Usage = SDL.GPUBufferUsageFlags.GraphicsStorageRead,
            });
        }



        public unsafe object CreateVertexBuffer(uint length, bool writeable, void* initialContent)
        {
            throw new NotImplementedException();
        }

        public void Destroy()
        {
            throw new NotImplementedException();
        }

        public void DestroyBuffer(BackendBufferAllocationReference buffer)
        {
            throw new NotImplementedException();
        }

        public void DestroyComputeShader(BackendComputeShaderReference shader)
        {
            throw new NotImplementedException();
        }

        public void DestroyDrawPipeline(BackendDrawPipelineReference pipeline)
        {
            throw new NotImplementedException();
        }

        public void DestroyFrameBufferObject(BackendFrameBufferObjectReference buffer)
        {
            throw new NotImplementedException();
        }

        public void DestroyFrameBufferPipeline(BackendFrameBufferPipelineReference backendRenderPassReference)
        {
            throw new NotImplementedException();
        }

        public void DestroyResourceSet(BackendResourceSetReference set)
        {
            throw new NotImplementedException();
        }

        public void DestroyShader(BackendShaderReference shader)
        {
            throw new NotImplementedException();
        }

        public void DestroyTexture(BackendTextureReference texture)
        {
            throw new NotImplementedException();
        }

        public void DestroyTextureSampler(BackendSamplerReference texture)
        {
            throw new NotImplementedException();
        }

        public void DispatchComputeShader(BackendComputeShaderReference shader, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            throw new NotImplementedException();
        }

        public void Draw(ReadOnlySpan<VertexAttributeDefinitionPlusBufferStruct> buffers, ReadOnlySpan<GCHandle<BackendResourceSetReference>> ResourceSets, BackendDrawPipelineReference pipeline, BackendIndexBufferAllocationReference indexbuffer, uint indexBufferOffset, IndexingDetails indexing)
        {
            throw new NotImplementedException();
        }

        public void EndDrawToScreen()
        {
            throw new NotImplementedException();
        }

        public void EndFrameBufferPipeline(BackendFrameBufferObjectReference fbo)
        {
            throw new NotImplementedException();
        }

        public void EndFrameRendering()
        {
            throw new NotImplementedException();
        }

        public void GenerateMipmaps(BackendTextureReference texture)
        {
            throw new NotImplementedException();
        }

        public ReadOnlySpan<byte> ReadTexturePixels(BackendTextureReference tex, uint level, EngineMath.Vector3<uint> offset, EngineMath.Vector3<uint> size)
        {
            throw new NotImplementedException();
        }

        public void SetScissor(EngineMath.Vector2<uint> offset, EngineMath.Vector2<uint> size)
        {
            throw new NotImplementedException();
        }

        public void StartDrawToScreen()
        {
            throw new NotImplementedException();
        }

        public void StartFrameRendering()
        {
            throw new NotImplementedException();
        }

        public void WaitForAllComputeShaders()
        {
            throw new NotImplementedException();
        }

        public void WriteTexturePixels(BackendTextureReference tex, uint level, EngineMath.Vector3<uint> offset, EngineMath.Vector3<uint> size, ReadOnlySpan<byte> content)
        {
            throw new NotImplementedException();
        }

        public void WriteToBuffer(BackendBufferAllocationReference buffer, ReadOnlySpan<WriteRange> writes, uint idx)
        {
            throw new NotImplementedException();
        }

        public void WriteToResourceSet(BackendResourceSetReference set, ReadOnlySpan<ResourceSetResourceBind> contents, uint idx)
        {
            throw new NotImplementedException();
        }
    }


}

#endif