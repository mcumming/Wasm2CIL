using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Collections.Generic;

namespace Wasm2CIL {
	public class WebassemblyFunctionBody
	{
		public readonly bool ElseTerminated;
		public readonly WebassemblyInstruction [] body;

		public int ParamCount;

		public void Emit (ILGenerator ilgen, int num_params)
		{
			this.ParamCount = num_params;
			Console.WriteLine ("body length: {0}", body.Length);
			foreach (var instr in body) {
				instr.Emit (ilgen, this);
			}
		}

		public WebassemblyFunctionBody (BinaryReader reader)
		{
			var body_builder = new List<WebassemblyInstruction> ();
			var label_stack = new List<WebassemblyControlInstruction> ();
			int depth = 0;

			// Incremented by control instructions
			int curr_label = 0;

			while (/*depth >= 0 && */(reader.BaseStream.Position != reader.BaseStream.Length)) {
				WebassemblyInstruction result = null;
				byte opcode = reader.ReadByte ();

				if (opcode <= WebassemblyControlInstruction.UpperBound ()) {
					// Needs the stack to turn indexes into references to basic blocks
					var control_result = new WebassemblyControlInstruction (opcode, reader, label_stack, ref curr_label);

					if (control_result.StartsBlock ()) {
						Console.WriteLine ("Start block");
						depth += 1;
						label_stack.Add (control_result);
					} else if (control_result.EndsBlock ()) {
						Console.WriteLine ("End block");
						// Tracks whether we've hit the extra 0x0b that marks end-of-function
						depth -= 1;
						if (label_stack.Count > 0)
							label_stack.RemoveAt (label_stack.Count - 1);
					} 
					result = control_result;
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

				if (result != null)
					body_builder.Add (result);

				if (depth >= 0)
					Console.WriteLine ("{0}{1}", new String (' ', depth + 1), result.ToString ());
				else
					Console.WriteLine (" {0}", result.ToString ());
			}

			this.body = body_builder.ToArray ();
		}
	}

	// Make parser return collection of basic blocks, not instructions

	public abstract class WebassemblyInstruction
	{
		public const byte End = 0x0b;
		public const byte Else = 0x05;

		public readonly byte opcode;

		public WebassemblyInstruction (byte opcode)
		{
			this.opcode = opcode;
		}

		public virtual string ToString () 
		{
			throw new Exception ("Must call instance copy of ToString for WebassemblyInstruction");
		}

		public abstract void Emit (ILGenerator ilgen, WebassemblyFunctionBody top_level);
	}

	public class WebassemblyControlInstruction : WebassemblyInstruction
	{
		Label label;
		public readonly int LabelIndex;

		ulong [] table;
		ulong index;
		ulong default_target;
		Type block_type;
		ulong function_index;
		ulong type_index;
		WebassemblyControlInstruction dest;

		bool loops;

		public readonly WebassemblyFunctionBody nested;
		public readonly WebassemblyFunctionBody else_section;

		public override void Emit (ILGenerator ilgen, WebassemblyFunctionBody top_level)
		{
			switch (opcode) {
				case 0x0:
					// Fixme: make this catchable / offer options at exception time
					ilgen.ThrowException (typeof (System.ExecutionEngineException));
					return;
				case 0x01:
					ilgen.Emit (OpCodes.Nop);
					return;

				case 0x02: // block
					label = ilgen.DefineLabel ();
					ilgen.MarkLabel (label);
					return;

				case 0x03: // loop
					label = ilgen.DefineLabel ();
					ilgen.MarkLabel (label);
					return;

				//case 0x04: // if 
					//return ilgen.Emit (OpCodes.Nop);
				//case 0x05: // Else
					//return ilgen.Emit (OpCodes.Nop);

				case 0x0b: // End
					if (this.dest != null && this.loops) {
						// jump to dest at end of body
						ilgen.Emit (OpCodes.Br, this.dest.GetLabel ());
					} else if (this.dest == null) {
						// ends function body, has implicit return
						ilgen.Emit (OpCodes.Ret);
					} else {
						ilgen.Emit (OpCodes.Nop);
					}
					return;

				// Br
				case 0x0c:
					ilgen.Emit (OpCodes.Br, this.dest.GetLabel ());
					return;

				// Br_if
				case 0x0d:
					ilgen.Emit (OpCodes.Brtrue, this.dest.GetLabel ());
					return;

				// Br_table
				//case 0x0e:
					//return ilgen.Emit (OpCodes.Nop);

				case 0x0f:
					ilgen.Emit (OpCodes.Ret);
					return;

				//// Call
				//case 0x10:
					//return ilgen.Emit (OpCodes.Nop);
				//case 0x11:
					//return ilgen.Emit (OpCodes.Nop);

				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
			return;
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
					if (this.dest == null)
						return String.Format ("end (Of Function)");
					else
						return String.Format ("end {0}", this.dest.GetLabelName ());
				case 0x0c:
					return String.Format ("br {0}", this.dest.GetLabelName ());
				case 0x0d:
					return String.Format ("br_if {0}", this.dest.GetLabelName ());
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

		public string GetLabelName ()
		{
			if (!this.StartsBlock ())
				throw new Exception ("Does not create label");

			return String.Format ("@{0}", this.LabelIndex);
		}

		public Label GetLabel ()
		{
			if (label == null && this.StartsBlock ())
				throw new Exception ("Did not emit label when traversing this instruction");
			else if (label == null)
				throw new Exception ("Does not create label");
			else
				return label;
		}

		public bool StartsBlock ()
		{
			return opcode == 0x02 || opcode == 0x03 || opcode == 0x04;
		}

		public bool EndsBlock ()
		{
			return opcode == 0x0b;
		}

		public static byte UpperBound ()
		{
			return 0x11;
		}

		public WebassemblyControlInstruction (byte opcode, BinaryReader reader, List<WebassemblyControlInstruction> labels, ref int labelIndex): base (opcode)
		{
			switch (this.opcode) {
				case 0x05: // else
				case 0x0B: // end
					if (labels.Count > 0)
						this.dest = labels [labels.Count - 1];
					break;
				case 0x0: // unreachable
				case 0x1: // nop
				case 0x0F: // return
					// No args
					break;
				case 0x0C: // br
				case 0x0D: // br_if
					// So these indexes are labels
					// each loop, block, 

					// All branching is to previous labels
					// This means that the most foolproof way to emit things is
					// to preverse all the ordering in the initial instruction stream.
					// When we emit this instruction, dest will already have had a label emitted.
					this.index = Parser.ParseLEBUnsigned (reader, 32);
					this.dest = labels [labels.Count - (int) index - 1];
					
					break;

				case 0x02: // block
					// need to make label 
					this.block_type = WebassemblyResult.Convert (reader);
					this.LabelIndex = labelIndex++;
					this.loops = false;
					break;

				case 0x03: // loop 
					// need to make label 
					this.block_type = WebassemblyResult.Convert (reader);
					this.LabelIndex = labelIndex++;
					this.loops = true;
					break;

				case 0x04: // if
					//this.block_type = reader.ReadByte ();
					//this.nested = new WebassemblyFunctionBody (reader);
					//if (this.nested.ElseTerminated)
						//this.else = new WebassemblyFunctionBody (reader);
					throw new Exception ("Implement me");

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

		public override void Emit (ILGenerator ilgen, WebassemblyFunctionBody top_level)
		{
			throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
		}

		public override string ToString () 
		{
			switch (opcode) {
				case 0x1a:
					// CIL: pop
					return "drop";
				case 0x1b:
					// CIL: dup + pop
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
		// If the index is lower than the number of parameters, this is a
		// local reference, else it is a parameter reference
		ulong index;

		void EmitGetter (ILGenerator ilgen, int num_params)
		{
			// Fixme: use packed encodings (_s) and the opcodes that mention the index

			if ((int) index < num_params) {
				ilgen.Emit (OpCodes.Ldarg, index);
				//Console.WriteLine ("ldarg {0}", index);
				return;
			}

			int labelIndex = (int) index - num_params;
			ilgen.Emit (OpCodes.Ldloc, labelIndex);
			//Console.WriteLine ("ldloc {0}", labelIndex);
		}

		void EmitSetter (ILGenerator ilgen, int num_params)
		{
			// Fixme: use packed encodings (_s) and the opcodes that mention the index

			if ((int) index < num_params) {
				ilgen.Emit (OpCodes.Starg, index);
			//Console.WriteLine ("starg {0}", index);
				return;
			}

			int labelIndex = (int) index - num_params;
			ilgen.Emit (OpCodes.Stloc, labelIndex);

			//Console.WriteLine ("stloc {0}", labelIndex);
		}

		public override void Emit (ILGenerator ilgen, WebassemblyFunctionBody top_level)
		{
			switch (opcode) {
				case 0x20:
					EmitGetter (ilgen, top_level.ParamCount);
					return;
				case 0x21:
					EmitSetter (ilgen, top_level.ParamCount);
					return;
				case 0x22:
					EmitSetter (ilgen, top_level.ParamCount);
					EmitGetter (ilgen, top_level.ParamCount);
					return;
				//case 0x23:
					//return String.Format ("get_global {0}", index);
				//case 0x24:
					//return String.Format ("set_global {0}", index);
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

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

		public override void Emit (ILGenerator ilgen, WebassemblyFunctionBody top_level)
		{
			throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
		}

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
		public override void Emit (ILGenerator ilgen, WebassemblyFunctionBody top_level)
		{
			switch (opcode) {
				case 0x41:
					ilgen.Emit (OpCodes.Ldc_I4, operand_i32);
					return;
				case 0x42:
					ilgen.Emit (OpCodes.Ldc_I8, operand_i64);
					return;
				case 0x43:
					ilgen.Emit (OpCodes.Ldc_R4, operand_f32);
					return;
				case 0x44:
					ilgen.Emit (OpCodes.Ldc_R8, operand_f64);
					return;

				case 0x45:
					ilgen.Emit (OpCodes.Ldc_I4, 0x0);
					ilgen.Emit (OpCodes.Ceq);
					return;
				case 0x46:
					ilgen.Emit (OpCodes.Ceq);
					return;
				case 0x47:
					ilgen.Emit (OpCodes.Ceq);
					ilgen.Emit (OpCodes.Neg);
					return;

				case 0x48:
					ilgen.Emit (OpCodes.Clt);
					return;
				case 0x49:
					ilgen.Emit (OpCodes.Clt_Un);
					return;

				case 0x4a:
					ilgen.Emit (OpCodes.Cgt);
					return;
				case 0x4b:
					ilgen.Emit (OpCodes.Cgt_Un);
					return;

				//case 0x4c: 
					//// Less than or equal, signed
					//// first - last > 0
					//// sub.ovf.un
					//return "i32.le_s";

				//case 0x4d:
					//// Less than or equal, unsigned
					//// first - last > 0
					//// sub.ovf.un
					//return "i32.le_u";

				//case 0x4e:
					//return "i32.ge_s";

				//case 0x4f:
					//return "i32.ge_u";

				//case 0x50:
					//return "i64.eqz";
				//case 0x51:
					//return "i64.eq";
				//case 0x52:
					//return "i64.ne";
				//case 0x53:
					//return "i64.lt_s";
				//case 0x54:
					//return "i64.lt_u";
				case 0x55:
					ilgen.Emit (OpCodes.Sub);
					ilgen.Emit (OpCodes.Ldc_I8, 0);
					ilgen.Emit (OpCodes.Cgt);
					return;

				//case 0x56:
					//return "i64.gt_u";
				//case 0x57:
					//return "i64.le_s";
				//case 0x58:
					//return "i64.le_u";
				//case 0x59:
					//return "i64.ge_s";
				//case 0x5a:
					//return "i64.ge_u";

				//case 0x5b:
					//return "f32.eq";
				//case 0x5c:
					//return "f32.ne";
				//case 0x5d:
					//return "f32.lt";
				//case 0x5e:
					//return "f32.gt";
				//case 0x5f:
					//return "f32.le";
				//case 0x60:
					//return "f32.ge";
				//case 0x61:
					//return "f64.eq";
				//case 0x62:
					//return "f64.ne";
				//case 0x63:
					//return "f64.lt";
				//case 0x64:
					//return "f64.gt";
				//case 0x65:
					//return "f64.le";
				//case 0x66:
					//return "f64.ge";
				//case 0x67:
					//return "i32.clz";
				//case 0x68:
					//return "i32.ctz";
				//case 0x69:
					//return "i32.popcnt";
				//case 0x6a:
					//return "i32.add";
				//case 0x6b:
					//return "i32.sub";
				//case 0x6c:
					//return "i32.mul";
				//case 0x6d:
					//return "i32.div_s";
				//case 0x6e:
					//return "i32.div_u";
				//case 0x6f:
					//return "i32.rem_s";
				//case 0x70:
					//return "i32.rem_u";
				//case 0x71:
					//return "i32.and";
				//case 0x72:
					//return "i32.or";
				//case 0x73:
					//return "i32.xor";
				//case 0x74:
					//return "i32.shl";
				//case 0x75:
					//return "i32.shr_s";
				//case 0x76:
					//return "i32.shr_u";
				//case 0x77:
					//return "i32.rotl";
				//case 0x78:
					//return "i32.rotr";
				//case 0x79:
					//return "i64.clz";
				//case 0x7a:
					//return "i64.ctz";
				//case 0x7b:
					//return "i64.popcnt";
				case 0x7c:
					ilgen.Emit (OpCodes.Add);
					return;
				case 0x7d:
					ilgen.Emit (OpCodes.Sub);
					return;
				case 0x7e:
					ilgen.Emit (OpCodes.Mul);
					return;
				//case 0x7f:
					//return "i64.div_s";
				//case 0x80:
					//return "i64.div_u";
				//case 0x81:
					//return "i64.rem_s";
				//case 0x82:
					//return "i64.rem_u";
				//case 0x83:
					//return "i64.and";
				//case 0x84:
					//return "i64.or";
				//case 0x85:
					//return "i64.xor";
				//case 0x86:
					//return "i64.shl";
				//case 0x87:
					//return "i64.shr_s";
				//case 0x88:
					//return "i64.shr_u";
				//case 0x89:
					//return "i64.rotl";
				//case 0x8a:
					//return "i64.rotr";
				//case 0x8b:
					//return "f32.abs";
				//case 0x8c:
					//return "f32.neg";
				//case 0x8d:
					//return "f32.ceil";
				//case 0x8e:
					//return "f32.floor";
				//case 0x8f:
					//return "f32.trunc";
				//case 0x90:
					//return "f32.nearest";
				//case 0x91:
					//return "f32.sqrt";
				//case 0x92:
					//return "f32.add";
				//case 0x93:
					//return "f32.sub";
				//case 0x94:
					//return "f32.mul";
				//case 0x95:
					//return "f32.div";
				//case 0x96:
					//return "f32.min";
				//case 0x97:
					//return "f32.max";
				//case 0x98:
					//return "f32.copysign";
				//case 0x99:
					//return "f64.abs";
				//case 0x9a:
					//return "f64.neg";
				//case 0x9b:
					//return "f64.ceil";
				//case 0x9c:
					//return "f64.floor";
				//case 0x9d:
					//return "f64.trunc";
				//case 0x9e:
					//return "f64.nearest";
				//case 0x9f:
					//return "f64.sqrt";
				//case 0xa0:
					//return "f64.add";
				//case 0xa1:
					//return "f64.sub";
				//case 0xa2:
					//return "f64.mul";
				//case 0xa3:
					//return "f64.div";
				//case 0xa4:
					//return "f64.min";
				//case 0xa5:
					//return "f64.max";
				//case 0xa6:
					//return "f64.copysign";
				//case 0xa7:
					//return "i32.wrap/i64";
				//case 0xa8:
					//return "i32.trunc_s/f32";
				//case 0xa9:
					//return "i32.trunc_u/f32";
				//case 0xaa:
					//return "i32.trunc_s/f64";
				//case 0xab:
					//return "i32.trunc_u/f64";

				case 0xac:
					ilgen.Emit (OpCodes.Conv_Ovf_U4);
					return;

				//case 0xad:
					//return "i64.extend_u/i32";
				//case 0xae:
					//return "i64.trunc_s/f32";
				//case 0xaf:
					//return "i64.trunc_u/f32";
				//case 0xb0:
					//return "i64.trunc_s/f64";
				//case 0xb1:
					//return "i64.trunc_u/f64";
				//case 0xb2:
					//return "f32.convert_s/i32";
				//case 0xb3:
					//return "f32.convert_u/i32";
				//case 0xb4:
					//return "f32.convert_s/i64";
				//case 0xb5:
					//return "f32.convert_u/i64";
				//case 0xb6:
					//return "f32.demote/f64";
				//case 0xb7:
					//return "f64.convert_s/i32";
				//case 0xb8:
					//return "f64.convert_u/i32";
				case 0xb9:
					ilgen.Emit (OpCodes.Conv_Ovf_U8);
					return;

				//case 0xba:
					//return "f64.convert_u/i64";
				//case 0xbb:
					//return "f64.promote/f32";
				//case 0xbc:
					//return "i32.reinterpret/f32";
				//case 0xbd:
					//return "i64.reinterpret/f64";
				//case 0xbe:
					//return "f32.reinterpret/i32";
				//case 0xbf:
					//return "f64.reinterpret/i64";
				default:
					throw new Exception (String.Format("Should not be reached: {0:X}", opcode));
			}
		}

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
				operand_i32 = Convert.ToInt32 (Parser.ParseLEBSigned (reader, 32));
			} else if (this.opcode == 0x42) {
				operand_i64 = Convert.ToInt64 (Parser.ParseLEBSigned (reader, 64));
			} else if (this.opcode == 0x43) {
				operand_f32 = reader.ReadSingle ();
			} else if (this.opcode == 0x44) {
				operand_f64 = reader.ReadDouble ();
			}
		}
	}
}



