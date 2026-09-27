using System;
using System.IO;
using System.Text;

namespace GothicClassicMount;

internal static class GothicWaveDecoder
{
	private static readonly int[] Steps = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
	private static readonly int[] IndexChanges = { -1,-1,-1,-1,2,4,6,8 };

	public static byte[] ToPcm( byte[] wav )
	{
		if ( wav.Length < 12 || Encoding.ASCII.GetString( wav, 0, 4 ) != "RIFF" || Encoding.ASCII.GetString( wav, 8, 4 ) != "WAVE" )
			throw new InvalidDataException( "Invalid WAV header." );
		int fmt = -1, fmtSize = 0, data = -1, dataSize = 0, frames = -1;
		for ( int p = 12; p + 8 <= wav.Length; )
		{
			int size = BitConverter.ToInt32( wav, p + 4 );
			if ( size < 0 || size > wav.Length - p - 8 ) throw new InvalidDataException( "Truncated WAV chunk." );
			var id = Encoding.ASCII.GetString( wav, p, 4 );
			if ( id == "fmt " ) { fmt = p + 8; fmtSize = size; }
			if ( id == "data" ) { data = p + 8; dataSize = size; }
			if ( id == "fact" && size >= 4 ) frames = BitConverter.ToInt32( wav, p + 8 );
			p += 8 + size + (size & 1);
		}
		if ( fmt < 0 || fmtSize < 16 || data < 0 ) throw new InvalidDataException( "Missing WAV format/data." );
		if ( BitConverter.ToUInt16( wav, fmt ) != 17 ) return wav;
		int channels = BitConverter.ToUInt16( wav, fmt + 2 );
		int rate = BitConverter.ToInt32( wav, fmt + 4 );
		int blockSize = BitConverter.ToUInt16( wav, fmt + 12 );
		if ( channels < 1 || channels > 2 || blockSize < channels * 4 || fmtSize < 20 || BitConverter.ToUInt16( wav, fmt + 14 ) != 4 )
			throw new InvalidDataException( "Unsupported IMA ADPCM format." );
		int samplesPerBlock = BitConverter.ToUInt16( wav, fmt + 18 );
		if ( samplesPerBlock != (blockSize - channels * 4) * 2 / channels + 1 )
			throw new InvalidDataException( "Invalid IMA block size." );
		using var pcm = new MemoryStream();
		using var output = new BinaryWriter( pcm );
		int written = 0;
		for ( int start = data; start < data + dataSize; start += blockSize )
		{
			int end = Math.Min( start + blockSize, data + dataSize );
			if ( end - start < channels * 4 ) throw new InvalidDataException( "Truncated IMA header." );
			var predictors = new int[channels];
			var indices = new int[channels];
			var samples = new short[channels][];
			var counts = new int[channels];
			for ( int c = 0; c < channels; c++ )
			{
				predictors[c] = BitConverter.ToInt16( wav, start + c * 4 );
				indices[c] = wav[start + c * 4 + 2];
				if ( indices[c] > 88 ) throw new InvalidDataException( "Invalid IMA step index." );
				samples[c] = new short[samplesPerBlock];
				samples[c][counts[c]++] = (short)predictors[c];
			}
			int pos = start + channels * 4;
			while ( pos < end )
				for ( int c = 0; c < channels && pos < end; c++ )
					for ( int b = 0; b < 4 && pos < end; b++ )
					{
						int value = wav[pos++];
						for ( int n = 0; n < 2; n++ )
						{
							int code = (value >> (n * 4)) & 15;
							int step = Steps[indices[c]];
							int delta = (step >> 3) + ((code & 1) != 0 ? step >> 2 : 0) + ((code & 2) != 0 ? step >> 1 : 0) + ((code & 4) != 0 ? step : 0);
							predictors[c] = Math.Clamp( predictors[c] + ((code & 8) != 0 ? -delta : delta), -32768, 32767 );
							indices[c] = Math.Clamp( indices[c] + IndexChanges[code & 7], 0, 88 );
							samples[c][counts[c]++] = (short)predictors[c];
						}
						}
			int count = counts[0];
			for ( int c = 1; c < channels; c++ ) count = Math.Min( count, counts[c] );
			if ( frames >= 0 ) count = Math.Min( count, frames - written );
			for ( int i = 0; i < count; i++ )
				for ( int c = 0; c < channels; c++ ) output.Write( samples[c][i] );
			written += count;
			if ( frames >= 0 && written >= frames ) break;
		}
		using var result = new MemoryStream();
		using var writer = new BinaryWriter( result );
		writer.Write( Encoding.ASCII.GetBytes( "RIFF" ) ); writer.Write( checked((int)pcm.Length + 36) );
		writer.Write( Encoding.ASCII.GetBytes( "WAVEfmt " ) ); writer.Write( 16 ); writer.Write( (short)1 ); writer.Write( (short)channels );
		writer.Write( rate ); writer.Write( rate * channels * 2 ); writer.Write( (short)(channels * 2) ); writer.Write( (short)16 );
		writer.Write( Encoding.ASCII.GetBytes( "data" ) ); writer.Write( (int)pcm.Length ); writer.Write( pcm.ToArray() );
		return result.ToArray();
	}
}
