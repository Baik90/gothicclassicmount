$ErrorActionPreference = 'Stop'

$zenkit = Join-Path $PSScriptRoot 'Editor\Libraries\ZenKit\ZenKit.dll'
$zenkitAssembly = [System.Reflection.Assembly]::LoadFrom( $zenkit )

$vfsType = $zenkitAssembly.GetType( 'ZenKit.Vfs', $true )
$overwriteType = $zenkitAssembly.GetType( 'ZenKit.VfsOverwriteBehavior', $true )
$worldType = $zenkitAssembly.GetType( 'ZenKit.World', $true )

$vfs = [Activator]::CreateInstance( $vfsType )
$older = [Enum]::Parse( $overwriteType, 'Older' )
$mountDisk = $vfsType.GetMethod( 'MountDisk', [Type[]]@( [string], $overwriteType ) )

Get-ChildItem 'E:\Steam\steamapps\common\Gothic\Data' -File -Filter *.VDF | ForEach-Object {
	$mountDisk.Invoke( $vfs, @( $_.FullName, $older ) ) | Out-Null
}

$resolve = $vfsType.GetMethod( 'Resolve', [Type[]]@( [string] ) )
$node = $resolve.Invoke( $vfs, @( '_WORK/DATA/WORLDS/WORLD.ZEN' ) )
if ( -not $node )
{
	throw 'WORLD.ZEN not resolved'
}

$buffer = $node.GetType().GetProperty( 'Buffer' ).GetValue( $node )
$world = [Activator]::CreateInstance( $worldType, @( $buffer ) )

'WORLD TYPE:'
$world.GetType().FullName

'WORLD PROPERTIES:'
$world.GetType().GetProperties() |
	Select-Object Name, @{ n = 'PropertyType'; e = { $_.PropertyType.FullName } } |
	Format-Table -AutoSize |
	Out-String -Width 220

'WORLD FIELDS:'
$world.GetType().GetFields() |
	Select-Object Name, @{ n = 'FieldType'; e = { $_.FieldType.FullName } } |
	Format-Table -AutoSize |
	Out-String -Width 220

'MESH TYPE:'
$mesh = $world.GetType().GetProperty( 'Mesh' ).GetValue( $world )
if ( $mesh ) { $mesh.GetType().FullName } else { '<null>' }

'WORLD STRING PROPERTIES WITH VALUES:'
$world.GetType().GetProperties() |
	Where-Object { $_.PropertyType -eq [string] } |
	ForEach-Object { '{0}={1}' -f $_.Name, $_.GetValue( $world ) }
