using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace GothicClassicMount;

internal sealed record GothicCharacterAnimation(string Name, float Fps, bool Looping, NMatrix[][] Frames, NVector3[] RootMotion);

internal sealed partial class GothicCharacterMeshReader
{
	// Curated gameplay clips; importing every HUMANS sequence would duplicate hundreds of large clips per NPC model.
	internal static readonly string[] DefaultAnimationNames =
	[
		// Verified Gothic 1 HUMANS.MDS sequences: locomotion starts, loops and transitions.
		"S_FIST", "S_WALK", "S_WALKL", "S_WALKWL", "S_RUN", "S_RUNL",
		"S_FISTWALKL", "S_FISTRUN", "S_FISTRUNL",
		"S_FLY", "S_FLYL", "S_SWIM", "S_SWIMF",
		"T_WALK_2_WALKL", "T_WALKL_2_WALK", "T_RUN_2_RUNL", "T_RUNL_2_RUN",
		"T_FISTWALK_2_FISTWALKL", "T_FISTWALKL_2_FISTWALK", "T_FISTRUN_2_FISTRUNL", "T_FISTRUNL_2_FISTRUN",
		// Turning while running and walking backwards (the quick backstep is a transition, not a loop).
		"T_RUNTURNL", "T_RUNTURNR", "T_WALKWTURNL", "T_WALKWTURNR", "T_WALKBL_2_WALK",
		"T_RUNSTRAFEL", "T_RUNSTRAFER", "T_WALKSTRAFEL", "T_WALKSTRAFER",
		// Jumping, falling, landing and getting back up.
		"S_JUMP", "S_JUMPUP", "S_JUMPUPLOW", "S_JUMPUPMID", "S_FALL", "S_FALLB", "S_FALLDN", "S_FALLEN", "S_FALLENB",
		"T_STAND_2_JUMP", "T_STAND_2_JUMPUP", "T_STAND_2_JUMPUPLOW", "T_STAND_2_JUMPUPMID",
		"T_JUMP_2_STAND", "T_JUMP_2_HANG", "T_JUMPB", "T_RUNL_2_JUMP", "T_RUNR_2_JUMP",
		"T_JUMPUPLOW_2_STAND", "T_JUMPUPMID_2_STAND", "T_FALL_2_FALLEN", "T_FALLB_2_FALLENB",
		"T_FALLDN_2_STAND", "T_FALLEN_2_STAND", "T_FALLENB_2_STAND", "S_DEAD", "S_DEADB", "T_DEAD", "T_DEADB",
		"S_1HATTACK", "S_2HATTACK", "S_FISTATTACK", "T_1HATTACKL", "T_1HATTACKR", "T_1HATTACKMOVE",
		"T_2HATTACKL", "T_2HATTACKR", "T_FISTATTACKMOVE", "T_GOTHIT",
		// Picking herbs and taking an item from a chest.
		"S_HERB_S0", "S_HERB_S1", "T_HERB_STAND_2_S0", "T_HERB_S0_2_STAND", "T_HERB_S0_2_S1", "T_HERB_S1_2_S0",
		"S_CHESTSMALL_S0", "S_CHESTSMALL_S1", "T_CHESTSMALL_STAND_2_S0", "T_CHESTSMALL_S0_PICKLEFT", "T_CHESTSMALL_S0_2_S1", "T_CHESTSMALL_S1_2_S0",
		"S_CHESTBIG_S0", "S_CHESTBIG_S1", "T_CHESTBIG_STAND_2_S0", "T_CHESTBIG_S0_PICKLEFT", "T_CHESTBIG_S0_2_S1", "T_CHESTBIG_S1_2_S0",
		"T_DOOR_FRONT_S0_PICKLEFT", "T_DOOR_FRONT_S0_PICKRIGHT", "T_DOOR_BACK_S0_PICKLEFT", "T_DOOR_BACK_S0_PICKRIGHT"
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
			var rootMotion=new NVector3[frameCount];
			var rootBone=Array.FindIndex(bones,b=>b.Parent<0);
			var rootOrigin=bones[rootBone].Local.Translation;
			var hasRootSample=false;
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
					// MAN quaternions use the inverse rotation convention of Numerics.
					var local=NMatrix.CreateFromQuaternion(Quaternion.Conjugate(Quaternion.Normalize(rotation)));
					local.Translation=position;
					var converted=ConvertBoneTransform(local);
					if(indices[n]==rootBone)
					{
						if(!hasRootSample) { rootOrigin=converted.Translation; hasRootSample=true; }
						var rootDelta=converted.Translation-rootOrigin;
						// Gothic uses a horizontal root trajectory for locomotion. Extract its
						// ground-plane motion for the actor, while retaining vertical animation
						// (jump/fall) in the skeleton pose.
						rootMotion[f]=new NVector3(rootDelta.X,rootDelta.Y,0);
						converted.Translation=new NVector3(rootOrigin.X,rootOrigin.Y,converted.Translation.Z);
					}
					// Root samples already include their height. Do not add MDH.RootTranslation again.
					frames[f][indices[n]]=converted;
				}
			}
			var looping = name.StartsWith("S_", StringComparison.OrdinalIgnoreCase) &&
				(name.EndsWith("L", StringComparison.OrdinalIgnoreCase) || name is "S_FIST" or "S_FLY" or "S_SWIM");
			result.Add(new(name,fps,looping,frames,rootMotion));
		}
		return _animations[definition.Visual]=result;
	}
}
