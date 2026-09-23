using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace GothicClassicMount;

internal sealed record GothicCharacterAnimation(string Name, float Fps, bool Looping, NMatrix[][] Frames);

internal sealed partial class GothicCharacterMeshReader
{
	// A small useful set per model, rather than duplicating thousands of clips for every NPC.
	internal static readonly string[] DefaultAnimationNames =
	[
		"S_RUN", "S_WALK", "S_FISTRUN", "S_FISTWALK", "S_IDLE", "S_STAND", "S_RUNL", "S_WALKL", "S_FISTRUNL", "S_FISTWALKL",
		"S_FLY", "S_FLYL", "S_SWIM", "S_SWIMF"
	];
	private readonly Dictionary<string, GothicCharacterBone[]> _skeletons = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, List<GothicCharacterAnimation>> _animations = new(StringComparer.OrdinalIgnoreCase);
	private static readonly NMatrix AxisSwap = new(1,0,0,0, 0,0,1,0, 0,1,0,0, 0,0,0,1);

	internal static NMatrix ConvertBoneTransform(NMatrix native)
	{
		var converted = AxisSwap * native * AxisSwap;
		converted.Translation *= 0.36f;
		return converted;
	}

	public GothicCharacterBone[] ReadSkeleton(GothicCharacterDefinition definition)
	{
		if(_skeletons.TryGetValue(definition.Visual,out var cached)) return cached;
		var hierarchy = Load("ModelHierarchy",Path.ChangeExtension(definition.Visual,".MDH"));
		var nodes = Items(Get(hierarchy,"Nodes"));
		if(nodes.Length == 0 || nodes.Length > 256) throw new InvalidDataException("Unsupported skeleton bone count.");
		var bones = new GothicCharacterBone[nodes.Length];
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for(int i=0;i<nodes.Length;i++)
		{
			var name=(string)Get(nodes[i],"Name");
			var parent=Convert.ToInt32(Get(nodes[i],"ParentIndex"));
			if(parent < -1 || parent >= i || !names.Add(name)) throw new InvalidDataException("Invalid skeleton hierarchy.");
			var local=NMatrix.Transpose((NMatrix)Get(nodes[i],"Transform"));
			if(parent<0) local.Translation += (NVector3)Get(hierarchy,"RootTranslation");
			local=ConvertBoneTransform(local);
			bones[i]=new(name,parent,local,local*(parent<0 ? NMatrix.Identity : bones[parent].Global));
		}
		return _skeletons[definition.Visual]=bones;
	}

	public IReadOnlyList<GothicCharacterAnimation> ReadAnimations(GothicCharacterDefinition definition)
	{
		if(_animations.TryGetValue(definition.Visual,out var cached)) return cached;
		var bones=ReadSkeleton(definition);
		var hierarchy=Load("ModelHierarchy",Path.ChangeExtension(definition.Visual,".MDH"));
		var checksum=unchecked((uint)Convert.ToInt32(Get(hierarchy,"Checksum")));
		var prefix=Path.GetFileNameWithoutExtension(definition.Visual)+"-";
		var result=new List<GothicCharacterAnimation>();
		foreach(var name in DefaultAnimationNames)
		{
			var filename=prefix+name+".MAN";
			if(!_files.ContainsKey(filename)) continue;
			var animation=Load("ModelAnimation",filename);
			if(Convert.ToUInt32(Get(animation,"Checksum"))!=checksum) throw new InvalidDataException($"Animation skeleton mismatch: {filename}");
			var indices=Items(Get(animation,"NodeIndices")).Select(Convert.ToInt32).ToArray();
			var samples=Items(Get(animation,"Samples"));
			var frameCount=Convert.ToInt32(Get(animation,"FrameCount"));
			var fps=Convert.ToSingle(Get(animation,"Fps"));
			if(frameCount<=0 || !float.IsFinite(fps) || fps<=0 || samples.Length!=frameCount*indices.Length || indices.Any(i=>i<0||i>=bones.Length) || indices.Distinct().Count()!=indices.Length)
				throw new InvalidDataException($"Invalid animation samples: {filename}");
			var frames=new NMatrix[frameCount][];
			for(int f=0;f<frameCount;f++)
			{
				frames[f]=bones.Select(b=>b.Local).ToArray();
				for(int n=0;n<indices.Length;n++)
				{
					var sample=samples[f*indices.Length+n];
					var rotation=(Quaternion)Field(sample,"Rotation");
					var position=(NVector3)Field(sample,"Position");
					if(!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
						!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared()<0.0001f)
						throw new InvalidDataException($"Non-finite animation transform: {filename}");
					// Locomotion previews run in place; keep vertical bob but remove horizontal travel.
					// A game controller moves the GameObject, rather than letting its mesh drift away.
					if(bones[indices[n]].Parent<0 && (name.EndsWith("L",StringComparison.Ordinal) || name=="S_SWIMF"))
					{
						var first=(NVector3)Field(samples[n],"Position");
						position.X=first.X; position.Z=first.Z;
					}
					// MAN quaternions use the inverse rotation convention of Numerics.
					var local=NMatrix.CreateFromQuaternion(Quaternion.Conjugate(Quaternion.Normalize(rotation)));
					local.Translation=position;
					// Root samples already include their height. Do not add MDH.RootTranslation again.
					frames[f][indices[n]]=ConvertBoneTransform(local);
				}
			}
			result.Add(new(name,fps,true,frames));
		}
		return _animations[definition.Visual]=result;
	}
}
