namespace GothicClassicMount;

internal struct GothicSkinnedVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector2 TexCoord;
	public Color32 BoneIndices;
	public Color32 BoneWeights;

	public static readonly VertexAttribute[] Layout =
	[
		new(VertexAttributeType.Position, VertexAttributeFormat.Float32),
		new(VertexAttributeType.Normal, VertexAttributeFormat.Float32),
		new(VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2),
		new(VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4),
		new(VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4)
	];
}
