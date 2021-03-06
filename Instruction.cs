using System;
using System.IO;
using System.Reflection.Emit;
using System.Collections.Generic;

namespace Wasm2CIL {
	
	public class WebassemblyInstructionBlock
	{
		public static WebassemblyInstruction [] Parse (BinaryReader reader) 
		{
			var accum = new List<WebassemblyInstruction> ();
			int depth = 0;

			while (depth >= 0 && (reader.BaseStream.Position != reader.BaseStream.Length)) {
				WebassemblyInstruction result = null;
				byte opcode = reader.ReadByte ();

				if (opcode <= WebassemblyControlInstruction.UpperBound ()) {
					result = new WebassemblyControlInstruction (opcode, reader);

					depth += ((WebassemblyControlInstruction ) result).DepthChange ();

				} else if (opcode <= WebassemblyParametricInstruction.UpperBound ()) {
					result = new WebassemblyParametricInstruction (opcode, reader);
				} else if (opcode <= WebassemblyVariableInstruction.UpperBound ()) {
					result = new WebassemblyVariableInstruction (opcode, reader);
				} else if (opcode <= WebassemblyMemoryInstruction.UpperBound ()) {
					result = new WebassemblyMemoryInstruction (opcode, reader);
				} else if (opcode <= WebassemblyNumericInstruction.UpperBound ()) {
					result = new WebassemblyNumericInstruction (opcode, reader);
				} else {
					throw new Exception (String.Format ("Illegal instruction {0:X}", opcode));
				}
				accum.Add (result);
				if (depth > 0)
					Console.WriteLine ("{0}{1}", new String (' ', depth), result.ToString ());
				else
					Console.WriteLine ("{0}: END", result.ToString ());
			}

			return accum.ToArray ();
		}
	}

	public abstract class WebassemblyInstruction
	{
		public const byte END = 0x0B;

		public readonly byte opcode;

		public WebassemblyInstruction (byte opcode)
		{
			this.opcode = opcode;
		}

		public virtual string ToString () 
		{
			throw new Exception ("Must call instance copy of ToString for WebassemblyInstruction");
		}

		public void Add (MethodBuilder builder) 
		{
			return;
		}
	}

	public class WebassemblyControlInstruction : WebassemblyInstruction
	{
		ulong [] table;
		ulong index;
		ulong default_target;
		ulong block_type;
		ulong function_index;
		ulong type_index;

		public int DepthChange () 
		{
			if (opcode == 0x0b)
				return -1;

			// Block, loop, if
			if (opcode == 0x02 || opcode == 0x03 || opcode == 0x04)
				return 1;

			// Everything else is unchanged
			return 0;
		}


		public override string ToString () 
		{
			switch (opcode) {
				case 0x0:
					return "unreachable";
				case 0x01:
					return "nop";
				case 0x02:
					return String.Format ("block {0}", block_type);
				case 0x03:
					return String.Format ("loop {0}", block_type);
				case 0x04:
					return String.Format ("if {0}", block_type);
				case 0x05:
					return "else";
				case 0x0b:
					return "end";
				case 0x0c:
					return String.Format ("br {0}", index);
				case 0x0d:
					return String.Format ("br_if {0}", index);
				case 0x0e:
					return "br_table";
				case 0x0f:
					return "return";
				case 0x10:
					return String.Format ("call {0}", this.function_index);
				case 0x11:
					return String.Format ("call_indirect {0}", this.type_index);
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

		public static byte UpperBound ()
		{
			return 0x11;
		}

		public WebassemblyControlInstruction (byte opcode, BinaryReader reader): base (opcode)
		{
			switch (this.opcode) {
				case 0x0: // unreachable
				case 0x1: // nop
				case 0x0F: // return
				case 0x05: // else
					break;
				case 0x0C: // br
				case 0x0D: // br_if
					this.index = Parser.ParseLEBUnsigned (reader, 32);
					break;
				case 0x02: // block
				case 0x03: // loop 
				case 0x04: // if
					this.block_type = Parser.ParseLEBUnsigned (reader, 32);
					break;
				case 0x0e: // br_table
					// works by getting index from stack. If index is in range of table,
					// we jump to the label at that index. Else go to default.
					this.table = Parser.ParseLEBUnsignedArray (reader);
					this.default_target = Parser.ParseLEBUnsigned (reader, 32);
					break;
				case 0x10: //call
					this.function_index = Parser.ParseLEBUnsigned (reader, 32);
					break;
				case 0x11: //call indirect
					this.type_index = Parser.ParseLEBUnsigned (reader, 32);
					var endcap = Parser.ParseLEBUnsigned (reader, 32);
					if (endcap != 0x0)
						throw new Exception ("Call indirect call not ended with 0x0");
					break;
				case 0x0B: // end
					// foo?
					break;
				default:
					throw new Exception (String.Format ("Control instruction out of range {0:X}", this.opcode));
			}
			
		}
	}

	public class WebassemblyParametricInstruction : WebassemblyInstruction
	{
		public static byte UpperBound ()
		{
			return 0x1B;
		}

		public override string ToString () 
		{
			switch (opcode) {
				case 0x1a:
					return "drop";
				case 0x1b:
					return "select";
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

		public WebassemblyParametricInstruction (byte opcode, BinaryReader reader): base (opcode)
		{
			if (this.opcode != 0x1A && this.opcode <= 0x1B) {
				throw new Exception ("Parametric opcode out of range");
			}
		}
	}

	public class WebassemblyVariableInstruction : WebassemblyInstruction
	{
		ulong index;

		public override string ToString () 
		{
			switch (opcode) {
				case 0x20:
					return String.Format ("get_local {0}", index);
				case 0x21:
					return String.Format ("set_local {0}", index);
				case 0x22:
					return String.Format ("tee_local {0}", index);
				case 0x23:
					return String.Format ("get_global {0}", index);
				case 0x24:
					return String.Format ("set_global {0}", index);
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

		public static byte UpperBound ()
		{
			return 0x24;
		}

		public WebassemblyVariableInstruction (byte opcode, BinaryReader reader): base (opcode)
		{
			if (this.opcode >= 0x20 && this.opcode <= 0x24) {
				this.index = Parser.ParseLEBUnsigned (reader, 32);
			} else if (this.opcode > 0x24) {
				throw new Exception ("Variable opcode out of range");
			}
		}
}

	public class WebassemblyMemoryInstruction : WebassemblyInstruction
	{
		ulong align;
		ulong offset;

		public override string ToString () 
		{
			switch (opcode) {
				case 0x28:
					return "i32.load";
				case 0x29:
					return "i64.load";
				case 0x2a:
					return "f32.load";
				case 0x2b:
					return "f64.load";
				case 0x2c:
					return "i32.load8_s";
				case 0x2d:
					return "i32.load8_u";
				case 0x2e:
					return "i32.load16_s";
				case 0x2f:
					return "i32.load16_u";
				case 0x30:
					return "i64.load8_s";
				case 0x31:
					return "i64.load8_u";
				case 0x32:
					return "i64.load16_s";
				case 0x33:
					return "i64.load16_u";
				case 0x34:
					return "i64.load32_s";
				case 0x35:
					return "i64.load32_u";
				case 0x36:
					return "i32.store";
				case 0x37:
					return "i64.store";
				case 0x38:
					return "f32.store";
				case 0x39:
					return "f64.store";
				case 0x3a:
					return "i32.store8";
				case 0x3b:
					return "i32.store16";
				case 0x3c:
					return "i64.store8";
				case 0x3d:
					return "i64.store16";
				case 0x3e:
					return "i64.store32";
				case 0x3f:
					return "current_memory";
				case 0x40:
					return "grow_memory";
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

		public static byte UpperBound ()
		{
			return 0x40;
		}

		public WebassemblyMemoryInstruction (byte opcode, BinaryReader reader): base (opcode)
		{
			if (this.opcode >= 0x28 && this.opcode <= 0x3E) {
				this.align = Parser.ParseLEBUnsigned (reader, 32);
				this.offset = Parser.ParseLEBUnsigned (reader, 32);
			} else if (this.opcode == 0x3F || this.opcode == 0x40) {
				var endcap = reader.ReadByte ();
				if (endcap != 0x0)
					throw new Exception ("Memory size instruction lackend null endcap");
			} else if (this.opcode > 0x40) {
				throw new Exception ("Memory opcode out of range");
			}
		}
	}

	public class WebassemblyNumericInstruction : WebassemblyInstruction
	{
		public override string ToString () 
		{
			switch (opcode) {
				case 0x41:
					return String.Format ("i32.const {0}", operand_i32);
				case 0x42:
					return String.Format ("i64.const {0}", operand_i64);
				case 0x43:
					return String.Format ("f32.const {0}", operand_f32);
				case 0x44:
					return String.Format ("f64.const {0}", operand_f64);
				case 0x45:
					return "i32.eqz";
				case 0x46:
					return "i32.eq";
				case 0x47:
					return "i32.ne";
				case 0x48:
					return "i32.lt_s";
				case 0x49:
					return "i32.lt_u";
				case 0x4a:
					return "i32.gt_s";
				case 0x4b:
					return "i32.gt_u";
				case 0x4c:
					return "i32.le_s";
				case 0x4d:
					return "i32.le_u";
				case 0x4e:
					return "i32.ge_s";
				case 0x4f:
					return "i32.ge_u";
				case 0x50:
					return "i64.eqz";
				case 0x51:
					return "i64.eq";
				case 0x52:
					return "i64.ne";
				case 0x53:
					return "i64.lt_s";
				case 0x54:
					return "i64.lt_u";
				case 0x55:
					return "i64.gt_s";
				case 0x56:
					return "i64.gt_u";
				case 0x57:
					return "i64.le_s";
				case 0x58:
					return "i64.le_u";
				case 0x59:
					return "i64.ge_s";
				case 0x5a:
					return "i64.ge_u";
				case 0x5b:
					return "f32.eq";
				case 0x5c:
					return "f32.ne";
				case 0x5d:
					return "f32.lt";
				case 0x5e:
					return "f32.gt";
				case 0x5f:
					return "f32.le";
				case 0x60:
					return "f32.ge";
				case 0x61:
					return "f64.eq";
				case 0x62:
					return "f64.ne";
				case 0x63:
					return "f64.lt";
				case 0x64:
					return "f64.gt";
				case 0x65:
					return "f64.le";
				case 0x66:
					return "f64.ge";
				case 0x67:
					return "i32.clz";
				case 0x68:
					return "i32.ctz";
				case 0x69:
					return "i32.popcnt";
				case 0x6a:
					return "i32.add";
				case 0x6b:
					return "i32.sub";
				case 0x6c:
					return "i32.mul";
				case 0x6d:
					return "i32.div_s";
				case 0x6e:
					return "i32.div_u";
				case 0x6f:
					return "i32.rem_s";
				case 0x70:
					return "i32.rem_u";
				case 0x71:
					return "i32.and";
				case 0x72:
					return "i32.or";
				case 0x73:
					return "i32.xor";
				case 0x74:
					return "i32.shl";
				case 0x75:
					return "i32.shr_s";
				case 0x76:
					return "i32.shr_u";
				case 0x77:
					return "i32.rotl";
				case 0x78:
					return "i32.rotr";
				case 0x79:
					return "i64.clz";
				case 0x7a:
					return "i64.ctz";
				case 0x7b:
					return "i64.popcnt";
				case 0x7c:
					return "i64.add";
				case 0x7d:
					return "i64.sub";
				case 0x7e:
					return "i64.mul";
				case 0x7f:
					return "i64.div_s";
				case 0x80:
					return "i64.div_u";
				case 0x81:
					return "i64.rem_s";
				case 0x82:
					return "i64.rem_u";
				case 0x83:
					return "i64.and";
				case 0x84:
					return "i64.or";
				case 0x85:
					return "i64.xor";
				case 0x86:
					return "i64.shl";
				case 0x87:
					return "i64.shr_s";
				case 0x88:
					return "i64.shr_u";
				case 0x89:
					return "i64.rotl";
				case 0x8a:
					return "i64.rotr";
				case 0x8b:
					return "f32.abs";
				case 0x8c:
					return "f32.neg";
				case 0x8d:
					return "f32.ceil";
				case 0x8e:
					return "f32.floor";
				case 0x8f:
					return "f32.trunc";
				case 0x90:
					return "f32.nearest";
				case 0x91:
					return "f32.sqrt";
				case 0x92:
					return "f32.add";
				case 0x93:
					return "f32.sub";
				case 0x94:
					return "f32.mul";
				case 0x95:
					return "f32.div";
				case 0x96:
					return "f32.min";
				case 0x97:
					return "f32.max";
				case 0x98:
					return "f32.copysign";
				case 0x99:
					return "f64.abs";
				case 0x9a:
					return "f64.neg";
				case 0x9b:
					return "f64.ceil";
				case 0x9c:
					return "f64.floor";
				case 0x9d:
					return "f64.trunc";
				case 0x9e:
					return "f64.nearest";
				case 0x9f:
					return "f64.sqrt";
				case 0xa0:
					return "f64.add";
				case 0xa1:
					return "f64.sub";
				case 0xa2:
					return "f64.mul";
				case 0xa3:
					return "f64.div";
				case 0xa4:
					return "f64.min";
				case 0xa5:
					return "f64.max";
				case 0xa6:
					return "f64.copysign";
				case 0xa7:
					return "i32.wrap/i64";
				case 0xa8:
					return "i32.trunc_s/f32";
				case 0xa9:
					return "i32.trunc_u/f32";
				case 0xaa:
					return "i32.trunc_s/f64";
				case 0xab:
					return "i32.trunc_u/f64";
				case 0xac:
					return "i64.extend_s/i32";
				case 0xad:
					return "i64.extend_u/i32";
				case 0xae:
					return "i64.trunc_s/f32";
				case 0xaf:
					return "i64.trunc_u/f32";
				case 0xb0:
					return "i64.trunc_s/f64";
				case 0xb1:
					return "i64.trunc_u/f64";
				case 0xb2:
					return "f32.convert_s/i32";
				case 0xb3:
					return "f32.convert_u/i32";
				case 0xb4:
					return "f32.convert_s/i64";
				case 0xb5:
					return "f32.convert_u/i64";
				case 0xb6:
					return "f32.demote/f64";
				case 0xb7:
					return "f64.convert_s/i32";
				case 0xb8:
					return "f64.convert_u/i32";
				case 0xb9:
					return "f64.convert_s/i64";
				case 0xba:
					return "f64.convert_u/i64";
				case 0xbb:
					return "f64.promote/f32";
				case 0xbc:
					return "i32.reinterpret/f32";
				case 0xbd:
					return "i64.reinterpret/f64";
				case 0xbe:
					return "f32.reinterpret/i32";
				case 0xbf:
					return "f64.reinterpret/i64";
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

		public static byte UpperBound ()
		{
			return 0xBF;
		}

		int operand_i32;
		long operand_i64;
		float operand_f32;
		double operand_f64;

		public WebassemblyNumericInstruction (byte opcode, BinaryReader reader): base (opcode)
		{
			if (this.opcode > 0xBF) {
				throw new Exception ("Numerical opcode out of range");
			} else if (this.opcode == 0x41) {
				operand_i32 = Convert.ToInt32 (reader.ReadInt32 ());
			} else if (this.opcode == 0x42) {
				operand_i64 = Convert.ToInt64 (reader.ReadInt64 ());
			} else if (this.opcode == 0x43) {
				operand_f32 = Convert.ToSingle (reader.ReadInt32 ());
			} else if (this.opcode == 0x44) {
				operand_f64 = Convert.ToDouble (reader.ReadInt64 ());
			}

		}
	}
}



