using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace GothicClassicMount;

public sealed class GothicCharacterDefinition
{
	public string Instance { get; set; }
	public string Name { get; set; }
	public string Visual { get; set; }
	public string Body { get; set; }
	public string Head { get; set; }
	public int BodyTexture { get; set; }
	public int Skin { get; set; }
	public int HeadTexture { get; set; }
	public int TeethTexture { get; set; }
	public string Armor { get; set; }
	public string ArmorVisual { get; set; }
	public int ArmorTexture { get; set; }
	public int Guild { get; set; }
	public int Level { get; set; }
	public float ScaleX { get; set; } = 1;
	public float ScaleY { get; set; } = 1;
	public float ScaleZ { get; set; } = 1;
	public string Error { get; set; }
}

// Kept independent of the editor so WorldInspect can exercise the actual importer.
internal static class GothicCharacterCatalog
{
	public static List<GothicCharacterDefinition> Read( Assembly zenkit, object vfs, string installDirectory )
	{
		var vmType = zenkit.GetType( "ZenKit.DaedalusVm", true );
		var npcType = zenkit.GetType( "ZenKit.Daedalus.NpcInstance", true );
		var itemType = zenkit.GetType( "ZenKit.Daedalus.ItemInstance", true );
		var node = vfs.GetType().GetMethod( "Resolve" ).Invoke( vfs, ["_WORK/DATA/SCRIPTS/_COMPILED/GOTHIC.DAT"] );
		var vm = node is not null ? Activator.CreateInstance( vmType, Get( node, "Buffer" ) )
			: Activator.CreateInstance( vmType, Path.Combine( installDirectory, "_work", "Data", "Scripts", "_compiled", "GOTHIC.DAT" ) );
		var callbacks = new List<Delegate>();
		var fallback = vmType.GetMethod( "RegisterExternalDefault" );
		var fallbackDelegate = MakeCallback( fallback.GetParameters()[0].ParameterType, _ => null );
		callbacks.Add( fallbackDelegate );
		fallback.Invoke( vm, [fallbackDelegate] );
		var init = vmType.GetMethods().Single( m => m.Name == "InitInstance" && m.IsGenericMethodDefinition && m.GetParameters()[0].ParameterType == typeof(string) );
		var definitions = new List<GothicCharacterDefinition>();
		GothicCharacterDefinition current = null;
		var equipment = new List<int>();
		Register( "MDL_SETVISUAL", [npcType, typeof(string)], a => { current.Visual = (string)a[1]; return null; } );
		Register( "MDL_SETVISUALBODY", [npcType, typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(int), typeof(int)], a =>
		{
			current.Body = (string)a[1]; current.BodyTexture = (int)a[2]; current.Skin = (int)a[3];
			current.Head = (string)a[4]; current.HeadTexture = (int)a[5]; current.TeethTexture = (int)a[6];
			if ( (int)a[7] > 0 ) equipment.Add( (int)a[7] );
			return null;
		} );
		Register( "EQUIPITEM", [npcType, typeof(int)], a => { equipment.Add( (int)a[1] ); return null; } );
		Register( "MDL_SETMODELSCALE", [npcType, typeof(float), typeof(float), typeof(float)], a =>
		{
			current.ScaleX = (float)a[1]; current.ScaleY = (float)a[2]; current.ScaleZ = (float)a[3]; return null;
		} );
		var symbols = (IEnumerable)vmType.GetMethod( "GetInstanceSymbols" ).Invoke( vm, ["C_NPC"] );
		foreach ( var symbol in symbols )
		{
			current = new GothicCharacterDefinition { Instance = (string)Get( symbol, "Name" ) };
			equipment.Clear();
			try
			{
				var npc = init.MakeGenericMethod( npcType ).Invoke( vm, [current.Instance] );
				var getName = npcType.GetMethod( "GetName" );
				current.Name = (string)getName.Invoke( npc, [Enum.ToObject( getName.GetParameters()[0].ParameterType, 0 )] );
				current.Guild = (int)Get( npc, "Guild" ); current.Level = (int)Get( npc, "Level" );
				foreach ( var id in equipment.Distinct().ToArray() )
				{
					var itemSymbol = vmType.GetMethod( "GetSymbolByIndex" ).Invoke( vm, [id] );
					var itemName = (string)Get( itemSymbol, "Name" );
					var item = init.MakeGenericMethod( itemType ).Invoke( vm, [itemName] );
					var visual = (string)Get( item, "VisualChange" );
					if ( !string.IsNullOrWhiteSpace( visual ) )
					{
						current.Armor = itemName; current.ArmorVisual = visual;
						current.ArmorTexture = (int)Get( item, "VisualSkin" );
					}
				}
				if ( string.IsNullOrWhiteSpace( current.Body ) ) current.Error = "No body visual in NPC initializer.";
			}
			catch ( Exception e ) { current.Error = (e.InnerException ?? e).Message; }
			definitions.Add( current );
		}
		GC.KeepAlive( callbacks ); GC.KeepAlive( vm );
		return definitions.OrderBy( d => d.Instance, StringComparer.OrdinalIgnoreCase ).ToList();

		void Register( string name, Type[] types, Func<object[], object> callback )
		{
			if ( vmType.GetMethod( "GetSymbolByName" ).Invoke( vm, [name] ) is null ) return;
			var method = vmType.GetMethods().Single( m => m.Name == "RegisterExternal" && m.IsGenericMethodDefinition
				&& m.GetGenericArguments().Length == types.Length && m.GetParameters()[1].ParameterType.Name.StartsWith( "ExternalFuncV`" ) ).MakeGenericMethod( types );
			var del = MakeCallback( method.GetParameters()[1].ParameterType, callback );
			callbacks.Add( del ); method.Invoke( vm, [name, del] );
		}
	}

	private static object Get( object obj, string name ) => obj?.GetType().GetProperty( name )?.GetValue( obj );
	private static Delegate MakeCallback( Type type, Func<object[], object> callback )
	{
		var parameters = type.GetMethod( "Invoke" ).GetParameters().Select( p => Expression.Parameter( p.ParameterType ) ).ToArray();
		var call = Expression.Invoke( Expression.Constant( callback ), Expression.NewArrayInit( typeof(object), parameters.Select( p => Expression.Convert( p, typeof(object) ) ) ) );
		return Expression.Lambda( type, Expression.Block( call, Expression.Empty() ), parameters ).Compile();
	}
}
