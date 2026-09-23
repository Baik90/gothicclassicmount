using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NVector3 = System.Numerics.Vector3;
using NVector2 = System.Numerics.Vector2;
using NMatrix = System.Numerics.Matrix4x4;

namespace GothicClassicMount;

internal sealed class GothicCharacterMeshPart
{
	public string Texture { get; set; }
	public NVector3[] Positions { get; set; }
	public NVector2[] Uvs { get; set; }
	public GothicBoneWeight[][] Weights { get; set; }
}

internal readonly record struct GothicBoneWeight(int Bone, float Weight);
internal sealed record GothicCharacterBone(string Name, int Parent, NMatrix Local, NMatrix Global);

// Converts native bind-pose geometry before it reaches the editor's model builder.
internal sealed partial class GothicCharacterMeshReader
{
	private readonly Assembly _assembly;
	private readonly object _vfs;
	private readonly Dictionary<string, string> _files = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, object> _assets = new( StringComparer.OrdinalIgnoreCase );
	public GothicCharacterMeshReader( Assembly assembly, object vfs )
	{
		_assembly = assembly; _vfs = vfs;
		Visit( Get( vfs, "Root" ), "" );
	}

	public List<GothicCharacterMeshPart> Read( GothicCharacterDefinition definition )
	{
		var hierarchy = Load( "ModelHierarchy", Path.ChangeExtension( definition.Visual, ".MDH" ) );
		var nodes = Items( Get( hierarchy, "Nodes" ) );
		var transforms = new NMatrix[nodes.Length];
		var root = (NVector3)Get( hierarchy, "RootTranslation" );
		for ( var i = 0; i < nodes.Length; i++ )
		{
			// ZenKit exposes ZenGin's column-vector matrices; Numerics uses row vectors.
			var local = NMatrix.Transpose( (NMatrix)Get( nodes[i], "Transform" ) );
			var parent = Convert.ToInt32( Get( nodes[i], "ParentIndex" ) );
			if ( parent >= i ) throw new InvalidDataException( "Invalid character skeleton parent order." );
			transforms[i] = local * (parent >= 0 ? transforms[parent] : NMatrix.CreateTranslation( root ));
		}
		var result = new List<GothicCharacterMeshPart>();
		var bodyName = string.IsNullOrEmpty( definition.ArmorVisual ) ? definition.Body : definition.ArmorVisual;
		var body = Load( "ModelMesh", Path.ChangeExtension( bodyName, ".MDM" ) );
		foreach ( var skin in Items( Get( body, "Meshes" ) ) )
		{
			var mesh = Get( skin, "Mesh" );
			var weights = Items( Get( skin, "Weights" ) );
			var positions = new NVector3[weights.Length];
			var influences = new GothicBoneWeight[weights.Length][];
			for ( var i = 0; i < weights.Length; i++ )
			{
				float sum = 0;
				var vertexWeights = new List<GothicBoneWeight>();
				foreach ( var entry in Items( weights[i] ) )
				{
					var index = Convert.ToInt32( Field( entry, "NodeIndex" ) );
					var weight = (float)Field( entry, "Weight" );
					if ( index < 0 || index >= transforms.Length || !float.IsFinite(weight) || weight < 0 ) throw new InvalidDataException( "Invalid skin weight." );
					positions[i] += NVector3.Transform( (NVector3)Field( entry, "Position" ), transforms[index] ) * weight;
					sum += weight;
					if (weight > 0) vertexWeights.Add(new(index,weight));
				}
				if ( sum <= 0 ) throw new InvalidDataException( "Character vertex has no bone weights." );
				positions[i] /= sum;
				influences[i] = vertexWeights.GroupBy(w=>w.Bone).Select(g=>new GothicBoneWeight(g.Key,g.Sum(w=>w.Weight)/sum)).OrderByDescending(w=>w.Weight).ToArray();
				if(influences[i].Length>4) throw new InvalidDataException("More than four skin influences are not supported.");
			}
			AddMesh( mesh, positions, NMatrix.Identity, false, influences, -1 );
		}
		foreach ( DictionaryEntry attachment in (IDictionary)Get( body, "Attachments" ) )
		{
			// The script's morph head replaces any head bundled with the armor mesh.
			if ( !string.IsNullOrWhiteSpace(definition.Head) && string.Equals((string)attachment.Key,"BIP01 HEAD",StringComparison.OrdinalIgnoreCase) ) continue;
			var index = Array.FindIndex( nodes, n => string.Equals( (string)Get(n,"Name"), (string)attachment.Key, StringComparison.OrdinalIgnoreCase ) );
			if ( index < 0 ) throw new InvalidDataException( $"Missing attachment bone {attachment.Key}." );
			AddMesh( attachment.Value, null, transforms[index], false, null, index );
		}
		if ( !string.IsNullOrWhiteSpace( definition.Head ) )
		{
			var head = Load( "MorphMesh", Path.ChangeExtension( definition.Head, ".MMB" ) );
			var index = Array.FindIndex( nodes, n => string.Equals( (string)Get(n,"Name"), "BIP01 HEAD", StringComparison.OrdinalIgnoreCase ) );
			if ( index < 0 ) throw new InvalidDataException( "Missing head attachment bone." );
			AddMesh( Get(head,"Mesh"), Items(Get(head,"MorphPositions")).Cast<NVector3>().ToArray(), transforms[index], true, null, index );
		}
		if ( result.Count == 0 ) throw new InvalidDataException( "No character geometry." );
		return result;

		void AddMesh( object mesh, NVector3[] positions, NMatrix transform, bool head, GothicBoneWeight[][] weights, int rigidBone )
		{
			positions ??= Items( Get(mesh,"Positions") ).Cast<NVector3>().ToArray();
			foreach ( var sub in Items( Get(mesh,"SubMeshes") ) )
			{
				var wedges = Items( Get(sub,"Wedges") );
				var vertices = new List<NVector3>(); var uvs = new List<NVector2>();
				var vertexWeights = new List<GothicBoneWeight[]>();
				foreach ( var triangle in Items( Get(sub,"Triangles") ) )
					foreach ( var corner in new[]{ "Wedge0", "Wedge1", "Wedge2" } )
					{
						var wedge = wedges[Convert.ToInt32(Field(triangle,corner))];
						var vertexIndex = Convert.ToInt32(Field(wedge,"Index"));
						var p = NVector3.Transform( positions[vertexIndex], transform );
						vertexWeights.Add(weights is null ? new[]{new GothicBoneWeight(rigidBone,1)} : weights[vertexIndex]);
						vertices.Add( new NVector3(p.X,p.Z,p.Y) * 0.36f );
						uvs.Add( (NVector2)Field(wedge,"Texture") );
					}
				var texture = (string)Get( Get(sub,"Material"), "Texture" );
				result.Add( new GothicCharacterMeshPart { Positions=vertices.ToArray(), Uvs=uvs.ToArray(), Weights=vertexWeights.ToArray(), Texture=TextureVariant(texture,definition,head) } );
			}
		}
	}

	private string TextureVariant( string texture, GothicCharacterDefinition d, bool head )
	{
		var name = Path.GetFileNameWithoutExtension(texture).ToUpperInvariant();
		var variant = head ? (name.Contains("TEETH") || name.Contains("MOUTH") ? d.TeethTexture : d.HeadTexture)
			: name.StartsWith("HUM_BODY") || name.StartsWith("BAB_BODY") || string.IsNullOrEmpty(d.ArmorVisual) ? d.BodyTexture : d.ArmorTexture;
		var varied = Regex.Replace( name, @"_V\d+", "_V" + Math.Max(0,variant) );
		var colored = Regex.Replace( varied, @"_C\d+", "_C" + Math.Max(0,d.Skin) );
		foreach ( var candidate in new[]{colored,varied,name} )
			if ( _files.TryGetValue(candidate+"-C.TEX",out var path) || _files.TryGetValue(candidate+".TEX",out path) ) return path;
		throw new FileNotFoundException( $"Character texture {texture} not found." );
	}

	private object Load( string type, string filename )
	{
		if ( string.IsNullOrEmpty(filename) || !_files.TryGetValue(Path.GetFileName(filename),out var path) ) throw new FileNotFoundException($"Character asset {filename} not found.");
		if ( _assets.TryGetValue(path,out var cached) ) return cached;
		var node = _vfs.GetType().GetMethod("Resolve").Invoke(_vfs,[path]);
		var native = Activator.CreateInstance(_assembly.GetType("ZenKit."+type,true), Get(node,"Buffer"));
		return _assets[path] = native.GetType().GetMethod("Cache").Invoke(native,null);
	}
	private void Visit( object node, string path )
	{
		foreach ( var child in Items(Get(node,"Children")) )
		{
			var name=(string)Get(child,"Name"); var full=path+name;
			if ((bool)child.GetType().GetMethod("IsDir").Invoke(child,null)) Visit(child,full+"/");
			else _files[name]=full;
		}
	}
	private static object Get( object value, string name ) => value.GetType().GetProperty(name).GetValue(value);
	private static object Field( object value, string name ) => value.GetType().GetField(name).GetValue(value);
	private static object[] Items( object value ) => ((IEnumerable)value).Cast<object>().ToArray();
}
