# A 3D game engine/framework written entirely in and around C#/NET 9.0.



## Features:

- Full platform/backend agnostic control over the entire rendering process

- An extensive easy to use metadata/generation system surrounding shader authoring and interaction 

- Complete support for custom object and resource types with easy to implement preprocessing logic

- Basic existing objects and resource types designed to be expanded upon

- Multithreading/async support

- A ton of helpful source generation

- Full single-codebase source to modify and extend freely

<br/>

- Debug builds come with hot-reloadable code/shaders/assets and ImGUI integration 

- Release builds come with shader precompilation, zstd asset compression, and target NativeAOT when published 





## Todo:

- Compute shaders (technically present but currently unfinished and untested)

<br/>

- Moderate further testing and bugfixing

- Semi-moderate codebase-wide cleanup

- More extensive annotation and documentation so that no part of the engine feels too deep or convoluted to casually understand

- Potentially a slightly less opaque customizable/extensible csproj basis (EntryProps.props)

<br/>

- Potentially some sort of easily-separable support for audio and physics



## Not included:

Any shader types outside of vertex/fragment/compute

Any kind of default editor or specifically imposed creation workflow





# Quick start:

To start, clone the repo, then install the following development-time cli dependencies:

| Dependency               |   Detection                                                                               |
| -------------------------| ----------------------------------------------------------------------------------------- |
| glslangvalidator         |   PATH variable                                                                           |
| spirv-cross              |   PATH variable                                                                           |
| Nvidia Texture Tools 3   |   copy path to nvtt_export.exe and supply to EngineSettings.cs/EngineSettings/NVTT3Path   |

Then open some of the demo projects to get a fundamental grasp of the engine, or create your own and simply import EntryProps via something like ``` <Import Project="..\EntryProps.props" /> ``` in your .csproj's Project body.



# Further info/troubleshooting

- Debug/release builds are categorized by presence of a DEBUG or RELEASE compilation symbol, NOT the name of the project configuration. This is so that other configurations like editor/tooling layouts can also be allowed to count as "debug builds".



# Demo images

<img width="1282" height="752" alt="triangle" src="https://github.com/user-attachments/assets/de1aa505-3bd5-4ab0-9043-cce0854c0464" />
<img width="1282" height="752" alt="cube" src="https://github.com/user-attachments/assets/146a1909-15ca-4b09-998c-f6e44d822c65" />

