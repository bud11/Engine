

using System.Numerics;
using static Engine.Core.EngineMath;


namespace Engine.Core;



/// <summary>
/// A simple BVH.
/// </summary>
public class BVH
{
    private readonly BVHNode Root;

    private BVH(BVHNode root)
    {
        Root = root;
    }


    public record class BVHNode
    (
        AABB Bounds,
        BVHNode Left,
        BVHNode Right,

        uint LeafIndexIfLeaf
    );



    public enum BVHCreationHeuristic
    {
        /// <summary>
        /// Fastest to create, least effective
        /// </summary>
        LongestAxisMedianSplit,

        /// <summary>
        /// Slowest to create, most effective
        /// </summary>
        SurfaceAreaHeuristic,
    }




    const int SAH_BUCKETS = 8;
    const int SAH_MIN_LEAF = 4; 


    private struct SAHBucket
    {
        public AABB Bounds;
        public int Count;
    }


    public static BVH Create(AABB[] bounds, BVHCreationHeuristic heuristic)
    {



        if (bounds == null || bounds.Length == 0)
            throw new ArgumentNullException();



        var root = Build(bounds, 0, bounds.Length, heuristic);
        return new BVH(root);



        static BVHNode Build(AABB[] bounds, int start, int count, BVHCreationHeuristic heuristic)
        {
            // Leaf node
            if (count == 1)
                return new BVHNode(bounds[start], null, null, (uint)start);

            // Compute bounding box of this node
            AABB nodeBounds = bounds[start];
            for (int i = 1; i < count; i++)
                nodeBounds = nodeBounds.Union(bounds[start + i]);

            return heuristic switch
            {
                BVHCreationHeuristic.LongestAxisMedianSplit => BuildMedian(bounds, start, count, nodeBounds),
                BVHCreationHeuristic.SurfaceAreaHeuristic => BuildSAH(bounds, start, count, nodeBounds),
                _ => throw new NotImplementedException(),
            };
        }




        // ------------------ MEDIAN SPLIT ------------------
        static BVHNode BuildMedian(AABB[] bounds, int start, int count, AABB nodeBounds)
        {
            // Longest axis
            var size = nodeBounds.Max - nodeBounds.Min;
            int axis = size.X > size.Y && size.X > size.Z ? 0 :
                       size.Y > size.Z ? 1 : 2;

            // Sort by center along axis
            Array.Sort(bounds, start, count, Comparer<AABB>.Create((a, b) =>
            {
                float ca = a.Center[axis];
                float cb = b.Center[axis];
                return ca.CompareTo(cb);
            }));

            int half = count / 2;
            var left = Build(bounds, start, half, BVHCreationHeuristic.LongestAxisMedianSplit);
            var right = Build(bounds, start + half, count - half, BVHCreationHeuristic.LongestAxisMedianSplit);

            return new BVHNode(nodeBounds, left, right, uint.MaxValue);
        }



        // ------------------ SURFACE AREA HEURISTIC ------------------
        static BVHNode BuildSAH(AABB[] bounds, int start, int count, AABB nodeBounds)
        {
            // Small ranges: SAH not worth it
            if (count <= SAH_MIN_LEAF)
                return BuildMedian(bounds, start, count, nodeBounds);

            int bestAxis = -1;
            int bestSplitBucket = -1;
            float bestCost = float.MaxValue;

            Vector3 extent = nodeBounds.Max - nodeBounds.Min;

            // Try X, Y, Z
            for (int axis = 0; axis < 3; axis++)
            {
                float axisExtent = extent[axis];
                if (axisExtent <= 1e-6f)
                    continue; // Degenerate axis

                // Init buckets
                Span<SAHBucket> buckets = stackalloc SAHBucket[SAH_BUCKETS];
                for (int i = 0; i < SAH_BUCKETS; i++)
                    buckets[i] = default;

                // Assign primitives to buckets
                for (int i = 0; i < count; i++)
                {
                    ref var aabb = ref bounds[start + i];
                    float center = aabb.Center[axis];

                    float t = (center - nodeBounds.Min[axis]) / axisExtent;
                    int bucket = Math.Clamp((int)(t * SAH_BUCKETS), 0, SAH_BUCKETS - 1);

                    if (buckets[bucket].Count == 0)
                        buckets[bucket].Bounds = aabb;
                    else
                        buckets[bucket].Bounds = buckets[bucket].Bounds.Union(aabb);

                    buckets[bucket].Count++;
                }

                // Prefix sums for SAH evaluation
                Span<AABB> leftBounds = stackalloc AABB[SAH_BUCKETS];
                Span<int> leftCount = stackalloc int[SAH_BUCKETS];

                Span<AABB> rightBounds = stackalloc AABB[SAH_BUCKETS];
                Span<int> rightCount = stackalloc int[SAH_BUCKETS];

                // Left → right
                for (int i = 0; i < SAH_BUCKETS; i++)
                {
                    if (i == 0)
                    {
                        leftCount[i] = buckets[i].Count;
                        leftBounds[i] = buckets[i].Bounds;
                    }
                    else
                    {
                        leftCount[i] = leftCount[i - 1] + buckets[i].Count;
                        leftBounds[i] = leftCount[i] == buckets[i].Count
                            ? buckets[i].Bounds
                            : leftBounds[i - 1].Union(buckets[i].Bounds);
                    }
                }

                // Right → left
                for (int i = SAH_BUCKETS - 1; i >= 0; i--)
                {
                    if (i == SAH_BUCKETS - 1)
                    {
                        rightCount[i] = buckets[i].Count;
                        rightBounds[i] = buckets[i].Bounds;
                    }
                    else
                    {
                        rightCount[i] = rightCount[i + 1] + buckets[i].Count;
                        rightBounds[i] = rightCount[i] == buckets[i].Count
                            ? buckets[i].Bounds
                            : rightBounds[i + 1].Union(buckets[i].Bounds);
                    }
                }

                // Evaluate splits between buckets
                for (int i = 0; i < SAH_BUCKETS - 1; i++)
                {
                    if (leftCount[i] == 0 || rightCount[i + 1] == 0)
                        continue;

                    float cost =
                        leftBounds[i].GetSurfaceArea() * leftCount[i] +
                        rightBounds[i + 1].GetSurfaceArea() * rightCount[i + 1];

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestSplitBucket = i;
                    }
                }
            }

            // Fallback: if SAH completely failed
            if (bestAxis == -1)
                return BuildMedian(bounds, start, count, nodeBounds);

            // Partition in-place by bucket
            float splitPos =
                nodeBounds.Min[bestAxis] +
                (bestSplitBucket + 1) * (extent[bestAxis] / SAH_BUCKETS);

            int mid = start;
            int end = start + count;

            for (int i = start; i < end; i++)
            {
                if (bounds[i].Center[bestAxis] < splitPos)
                {
                    (bounds[i], bounds[mid]) = (bounds[mid], bounds[i]);
                    mid++;
                }
            }

            int leftCountFinal = mid - start;
            if (leftCountFinal == 0 || leftCountFinal == count)
                return BuildMedian(bounds, start, count, nodeBounds);

            var left = Build(bounds, start, leftCountFinal, BVHCreationHeuristic.SurfaceAreaHeuristic);
            var right = Build(bounds, mid, count - leftCountFinal, BVHCreationHeuristic.SurfaceAreaHeuristic);

            return new BVHNode(nodeBounds, left, right, uint.MaxValue);
        }

    }

/*

#if DEBUG

    public static byte[] Serialize(BVH bvh)
    {

        List<byte> final = new();

        push(bvh.Root, final);

        static void push(BVHNode node, List<byte> final)
        {
            final.AddRange(Parsing.SerializeType(node.Bounds.Min));
            final.AddRange(Parsing.SerializeType(node.Bounds.Max));

            final.AddRange(BitConverter.GetBytes(node.LeafIndexIfLeaf));

            if (node.LeafIndexIfLeaf == uint.MaxValue)
            {
                push(node.Left, final);
                push(node.Right, final);
            }
        }

        return final.ToArray();
    }

#endif

    public static BVH Deserialize(BinaryReader src)
    {
        return new BVH(read(src));


        static BVHNode read(BinaryReader src)
        {
            var bounds = AABB.FromMinMax(src.DeserializeKnownType<Vector3>(), src.DeserializeKnownType<Vector3>());

            var idx = src.ReadUInt32();

            BVHNode left = null;
            BVHNode right = null;

            if (idx != uint.MaxValue)
            {
                left = read(src);
                right = read(src);
            }

            return new BVHNode(bounds, left, right, idx);

        }

    }
*/




    public void Query(in AABB query, ref Span<AABB> buffer)
    {
        int count = 0;
        QueryNode(Root, query, ref buffer, ref count);
        buffer = buffer[..count];


        static void QueryNode(
            BVHNode node,
            in AABB query,
            ref Span<AABB> buffer,
            ref int count)
        {
            if (node == null)
                return;

            if (!node.Bounds.Overlaps(query))
                return;

            // Leaf
            if (node.Left == null && node.Right == null)
            {
                if (count < buffer.Length)
                    buffer[count++] = node.Bounds;
                return;
            }

            QueryNode(node.Left, query, ref buffer, ref count);
            QueryNode(node.Right, query, ref buffer, ref count);
        }


    }

}

