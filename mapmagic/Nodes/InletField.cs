using System;
using System.Reflection;
using System.Collections.Generic;

using MapMagic.Products;
using UnityEngine;
using Den.Tools;


namespace MapMagic.Nodes
{
	[Serializable]
	public struct FieldChArrInfo
	/// Tuple to send fieldChArr together with channel and array index
	{
		public FieldInfo field;
		public int channel;
		public int arrIndex;

		public FieldChArrInfo (FieldInfo field, int ch=-1, int ai=-1)
			{ this.field = field; channel=ch; arrIndex=ai; }

		public override bool Equals (object obj) 
		{
			if (obj is FieldChArrInfo other)
				return field == other.field && channel == other.channel && arrIndex == other.arrIndex;
			return false;
		}

		public override int GetHashCode () => HashCode.Combine(field, channel, arrIndex);

		public static bool operator == (FieldChArrInfo left, FieldChArrInfo right) =>  
			left.field == right.field && 
			left.channel == right.channel && 
			left.arrIndex == right.arrIndex;
		public static bool operator != (FieldChArrInfo left, FieldChArrInfo right) => !(left == right);
	}


	[Serializable]
	public class FieldInlets : ISerializationCallbackReceiver
	{
		[Serializable] 
		public class MathInletField : MathInlet
		{
			public FieldChArrInfo fieldArrCh; 
		}

		private MathInletField[] fields;


		public bool HasFieldInlets => fields != null  &&  fields.Length != 0;

		public bool Contains (FieldChArrInfo fieldChArr) => FindIndex(fieldChArr) >= 0;

		public bool Contains (FieldInfo field) => FindIndex(new FieldChArrInfo(field)) >= 0;

		public bool Contains (FieldChArrInfo fieldChArr, out int index)
		{
			index = FindIndex(fieldChArr);
			return index >= 0;
		}

		public int FindIndex (FieldChArrInfo fieldArrCh)
		{
			if (!HasFieldInlets)
				return -1;

			for (int i=0; i<fields.Length; i++)
				if (fields[i].fieldArrCh == fieldArrCh)
					return i;

			return -1;
		}

		public MathInlet FindInlet (FieldChArrInfo fieldChArr)
		{
			int index = FindIndex(fieldChArr);
			if (index >= 0)
				return fields[index];
			else
				return null;
		}


		public IEnumerable<MathInlet> Inlets ()
		{
			for (int i=0; i<fields.Length; i++)
				yield return fields[i]; 
		}

		
		public void ConvertToInlet (Generator gen, FieldChArrInfo fieldChArr)
		///Creates new inlet for this fieldChArr
		{
			if (Contains(fieldChArr))
				return;

			MathInletField infi = (MathInletField)IInlet<object>.Create(typeof(MathInletField), gen);
			infi.fieldArrCh = fieldChArr;

			if (fields == null)
				fields = new MathInletField[0];

			ArrayTools.Add(ref fields, infi);
		}


		public void ConvertToField (FieldChArrInfo fieldChArr, Graph graph)
		///Removes inlet and converts back to field
		///Grpah to inlink this inlet
		{
			if (!Contains(fieldChArr, out int index))
				return;

			graph.UnlinkInlet(fields[index]);

			ArrayTools.RemoveAt(ref fields, index);

			if (fields.Length == 0)
				fields = null;
		}


		public void ReadFieldInlets (Generator gen, TileData data, StopToken stop)
		///Reads all generated inlet fields and assigns values from data to this generator
		///Same as Generate, but happens beforehand
		///Executed by graph right before Generate
		{
			if (fields == null  ||  fields.Length == 0)
					return;

			Type genType = gen.GetType();

			foreach (MathInletField infi in fields)
			{
				if (stop!=null && stop.stop) 
					return;

				Calculator.Vector vec = data.ReadInletProduct(infi);
				if (vec == null) 
					continue;

				SetValue(gen, infi.fieldArrCh, vec, genType);
			}
		}


		private void SetValue (Generator gen, FieldChArrInfo entry, Calculator.Vector vec, Type genType=null)
		///Same as FieldInfo.SetValue, but takes InletField 
		{
			if (genType==null)
				genType = gen.GetType(); //better provide it externally to avoid getting mode each 

			FieldInfo fieldInfo = genType.GetField(entry.field.Name);
			if (fieldInfo == null)
				return; //might happen in biome, which has not fields, but ovd exposed

			if (fieldInfo.FieldType != entry.field.FieldType  &&  !fieldInfo.FieldType.IsArray)
				throw new Exception("Recorded field type doesn't match generator field type. Possibly generator has changed.");

			object val;
			if (entry.channel == -1) //non-channel
				val = vec.Convert(entry.field.FieldType);
			else 
			{
				object origVal = fieldInfo.GetValue(gen); //loading original value to take other channels
				val = vec.ConvertToChannel(origVal, entry.channel, entry.field.FieldType);
			}

			if (entry.arrIndex == -1) //non-array
				fieldInfo.SetValue(gen, val);
			else if (gen is MatrixGenerators.Blend200 blendGen) //special case for blend node //TODO: do we still need it?
				blendGen.layers[entry.arrIndex].opacity = (float)val;
			else //array
			{
				Array arr = (Array)fieldInfo.GetValue(gen);
				arr.SetValue(val, entry.arrIndex);
				fieldInfo.SetValue(gen, arr);
			}
		}

		#region Serialization

			[SerializeField] private string[] fieldNames;
			[SerializeField] private string[] typeNames;
			[SerializeField] private int[] serChNums;
			[SerializeField] private int[] serArrIndexes;
			[SerializeField] private ulong[] serIds;
			[SerializeField] private string[] serGuiNames;
			[SerializeField, SerializeReference] public Generator serGen;

			public void OnBeforeSerialize ()
			{
				if (fields == null || fields.Length == 0)
				{
					fieldNames = null;
					typeNames = null;
					serChNums = null;
					serArrIndexes = null;
					serIds = null;
					serGuiNames = null; 
					return;
				}

				fieldNames = new string[fields.Length];
				typeNames = new string[fields.Length];
				serChNums = new int[fields.Length];
				serArrIndexes = new int[fields.Length];
				serIds = new ulong[fields.Length];
				serGuiNames = new string[fields.Length];

				for (int i = 0; i < fields.Length; i++)
				{
					fieldNames[i] = fields[i].fieldArrCh.field.Name;
					typeNames[i] = fields[i].fieldArrCh.field.DeclaringType.FullName;
					serChNums[i] = fields[i].fieldArrCh.channel;
					serArrIndexes[i] = fields[i].fieldArrCh.arrIndex;
					serIds[i] = fields[i].id;
					serGuiNames[i] = fields[i].guiName;
					serGen = fields[i].Gen;
				}
			}

			public void OnAfterDeserialize ()
			{
				if (fieldNames == null || fieldNames.Length == 0) 
				{
					fields = null;
					return;
				}

				fields = new MathInletField[fieldNames.Length];
				for (int i = 0; i < fieldNames.Length; i++)
				{
					string typeName = typeNames[i];
					string fieldName = fieldNames[i];
					int channel = serChNums[i];
					int arrIndex = serArrIndexes[i];

					Type type = Type.GetType(typeName);
					FieldInfo fieldInfo = type?.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

					if (fieldInfo != null)
					{
						fields[i] = new MathInletField
						{
							fieldArrCh = new FieldChArrInfo(fieldInfo, channel, arrIndex), 
							id = serIds[i],
							guiName = serGuiNames[i]
						};
						fields[i].SetGen(serGen);
					}
				}
			}

		#endregion
	}
}
