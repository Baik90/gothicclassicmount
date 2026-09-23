using System;
using System.IO;
using System.Linq;
using NMatrix = System.Numerics.Matrix4x4;

namespace GothicClassicMount;

public sealed partial class GothicClassicMount
{
	internal void ValidateCharacterRig(GothicCharacterDefinition definition, Model model, bool animations)
	{
		var bones=_characterMeshes.ReadSkeleton(definition);
		if(model.BoneCount!=bones.Length) throw new InvalidDataException("Bone count mismatch.");
		for(int i=0;i<bones.Length;i++)
		{
			if(model.GetBoneName(i)!=bones[i].Name || model.GetBoneParent(i)!=bones[i].Parent) throw new InvalidDataException("Bone order mismatch.");
			CheckTransform(model.GetBoneTransform(i),CharacterTransform(bones[i].Global),$"bind bone {i}");
		}
		var clips=_characterMeshes.ReadAnimations(definition);
		if(clips.Count==0 || clips.Any(c=>!model.AnimationNames.Contains(c.Name))) throw new InvalidDataException("Missing animation clips.");
		if(!animations) return;
		var world=new SceneWorld();
		try
		{
			var sceneModel=new SceneModel(world,model,Transform.Zero) { UseAnimGraph=false, PlaybackRate=0 };
			foreach(var clip in clips)
			{
				sceneModel.CurrentSequence.Name=clip.Name;
				foreach(var frameIndex in new[]{0,clip.Frames.Length/2,Math.Max(0,clip.Frames.Length-2)}.Distinct())
				{
					sceneModel.CurrentSequence.Time=frameIndex/clip.Fps;
					sceneModel.Update(0);
					var globals=new NMatrix[bones.Length];
					for(int i=0;i<bones.Length;i++)
					{
						globals[i]=clip.Frames[frameIndex][i]*(bones[i].Parent<0 ? NMatrix.Identity : globals[bones[i].Parent]);
						CheckTransform(sceneModel.GetBoneWorldTransform(i),CharacterTransform(globals[i]),$"{clip.Name} frame {frameIndex} bone {i}");
					}
				}
			}
		}
		finally {world.Delete();}
	}
	private static void CheckTransform(Transform actual, Transform expected, string label)
	{
		if(Vector3.DistanceBetween(actual.Position,expected.Position)>0.05f ||
			Vector3.DistanceBetween(actual.Rotation.Forward,expected.Rotation.Forward)>0.005f ||
			Vector3.DistanceBetween(actual.Rotation.Up,expected.Rotation.Up)>0.005f)
			throw new InvalidDataException($"Skeleton transform mismatch ({label}): actual={actual}, expected={expected}");
	}
}
