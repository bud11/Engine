# A 3D game engine/framework/runtime written in and around C#/NET 9.0. <br/> 🚧 In development 🚧

This project aims to offer a platform agnostic and mostly unopinionated foundation to build out any kind of engine or game, rapidly, without sacrificing control nor requiring immense boilerplate/knowledge.


## Features:

- Full platform/backend agnostic control over the entire rendering process (currently featuring a Vulkan backend)
- An extensive easy to use shader metadata/authoring system with support for multiple shader languages (currently featuring GLSL and HLSL)
- Complete support for custom object and resource types
- A robust easy to use serialization/deserialization system fit for all types of data (for example, resources, save data, etc)
- Extensive multithreading/async support
- Fully unified single-language source to modify and extend freely where that need may arise

## Conditional Features:
- <b>Debug builds</b> come with hot-reloadable code/shaders/assets and ImGUI integration 
- <b>Release builds</b> come with shader precompilation, zstd asset compression, and can safely target NativeAOT when published 

## Todos/considerations:
- Some mild rendering backend abstraction reworking to make it feel more robust, extensible and less vulkan-centric
- Finishing compute shaders properly, and in tandem, solidifying asynchronous gpu work and gpu -> cpu communication
- Likely some form of rendering pipeline rework, such that materials are moreso compiled/cached rather than assembled fresh
- Further work on more optimal resource/subresource compilation, resource dependencies


# Quick start:

To start, clone the repo, then install some or all of the following development-time cli dependencies:

| Dependency                                                | Nessecity                             |  Detection                                                                                 |
| ----------------------------------------------------------|---------------------------------------|------------------------------------------------------------------------------------------- |
| spirv-cross                                               | Nessecary                                      | PATH variable                                                                     |
| glslangvalidator                                          | Only for GLSL                                      | PATH variable                                                                 |
| dxc                                                       | Only for HLSL                                      | PATH variable                                                                              |
| AMD Compressonator (does not require an AMD gpu)          | Interchangeable with NVTT3, one required            | PATH variable (to the directory containing compressonatorcli.exe)                          |
| Nvidia Texture Tools 3 (does not require an nvidia gpu)   | Interchangeable with compressonator, one required   | PATH variable (to the directory containing nvtt_export.exe)                                |

Then open some of the demo projects to get a fundamental grasp of the engine, or create your own and simply import EntryProps via something like ``` <Import Project="..\EntryProps.props" /> ``` in your .csproj's Project body.



# Tools:

A blender addon aiming to be capable of supporting a full production pipeline is avaliable here: https://github.com/bud11/Engine-Blender-Tools



# Demo images

This section will be further populated with more advanced demos as they become available within the demos folder.

Manually drawn NDC triangle:
<img width="1282" height="752" alt="triangle" src="https://github.com/user-attachments/assets/de1aa505-3bd5-4ab0-9043-cce0854c0464" />
Code-generated cube scene, camera and basic scene rendering pipeline:
<img width="1282" height="752" alt="cube" src="https://github.com/user-attachments/assets/146a1909-15ca-4b09-998c-f6e44d822c65" />
Intel sponza scene imported via blender addon:
<img width="2477" height="1256" alt="initialsponza" src="https://github.com/user-attachments/assets/70a8f001-b15d-4918-93b8-ea11fb0920ad" />

