# SDF Terrain Generation Demo
### Overview
This is a demo project I made to experiment with terrain generation methods in Unity as an improvement to my previous attempt found here: https://github.com/billiam-dev/Isosurface-Generation
This version supports much improved editor performance and an infinite world.
Note that this project is incomplete and not for use in production, but can be used freely as reference.

### Details
- Both the density field generation and isosurface meshing components are implemented with Unity JOBS.
- It manages LODs using a brickmap system with 5 levels of detail.
- It uses a modified Marching Cubes algorithm known as Transvoxel to stitch the gaps between LODs.

### Features
- Mult-octave 2D noise layers for terrain surface.
- 3D noise layers for surface and caves.
- Realtime SDF shape editor for cave generation or terrain construction.

### Sources
- https://paulbourke.net/geometry/polygonise/
- https://transvoxel.org/
- https://eetumaenpaa.fi/blog/marching-cubes-optimizations-in-unity/#voxelcorners-vs-stackalloc
- https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu
- https://iquilezles.org/
