namespace GothicClassicMount;

internal struct GothicVertex
{
	[VertexLayout.Position] public Vector3 position;
	[VertexLayout.Normal] public Vector3 normal;
	[VertexLayout.TexCoord] public Vector2 texcoord;

	public static readonly VertexAttribute[] Layout =
	[
		new VertexAttribute( VertexAttributeType.Position, VertexAttributeFormat.Float32 ),
		new VertexAttribute( VertexAttributeType.Normal, VertexAttributeFormat.Float32 ),
		new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 )
	];
}
