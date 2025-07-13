using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
//using UnityEngine.Profiling;

using Den.Tools;

using MapMagic.Core;
using MapMagic.Products;
using UnityEditor;

namespace MapMagic.Nodes
{
	[System.Serializable]
	[GeneratorMenu (name = "Unknown", iconName="GeneratorIcons/Generator")]
	public class MissingGenPlug : Generator, IMultiInlet, IMultiOutlet, ISerializationCallbackReceiver
	{
		public override (string, int) GetCodeFileLine () => GetCodeFileLineBase();  //to get here with right-click on generator

		public string assemblyName;
		public string namespaceName;
		public string className;
		public long referenceId;
		public string serializedData;

		[NonSerialized] public IInlet<object>[] inlets;
		public IEnumerable<IInlet<object>> Inlets() 
		{ 
			for (int i=0; i<inlets.Length; i++)
				yield return inlets[i];
		}

		[NonSerialized] public IOutlet<object>[] outlets;
		public IEnumerable<IOutlet<object>> Outlets() 
		{ 
			for (int i=0; i<outlets.Length; i++)
				yield return outlets[i];
		}


		public static ulong GetMissingId (string serializedData) =>
			SerializedDataReader.GetValueFromSerializedData<ulong>("id", serializedData);

		public static MissingGenPlug CreatePlugFromData (string assemblyName, string namespaceName, string className, long referenceId, string serializedData)
		{
			MissingGenPlug miss = new MissingGenPlug() {
				id = SerializedDataReader.GetValueFromSerializedData<ulong>("id", serializedData),
				guiPosition = SerializedDataReader.GetValueFromSerializedData<Vector2>("guiPosition", serializedData),
				assemblyName = assemblyName,
				namespaceName = namespaceName,
				className = className,
				referenceId = referenceId,
				serializedData = serializedData
				};

			string[] inletTypeNames = SerializedDataReader.GetValueFromSerializedData<string[]>("serializedMissingInletTypes", serializedData);
			ulong[] inletIds = SerializedDataReader.GetValueFromSerializedData<ulong[]>("serializedMissingInletIds", serializedData);
			miss.inlets = new IInlet<object>[inletTypeNames.Length];
			for (int i=0; i<inletTypeNames.Length; i++)
			{
				Type type = Generator.GetGenericType(inletTypeNames[i]);
				Type genericType = typeof(Inlet<>).MakeGenericType(type);
				miss.inlets[i] =  (IInlet<object>)Activator.CreateInstance(genericType);
				miss.inlets[i].Id = inletIds[i];
				miss.inlets[i].SetGen(miss);
			}

			string[] outletTypeNames = SerializedDataReader.GetValueFromSerializedData<string[]>("serializedMissingInletTypes", serializedData);
			ulong[] outletIds = SerializedDataReader.GetValueFromSerializedData<ulong[]>("serializedMissingOutletIds", serializedData);
			miss.outlets = new IOutlet<object>[outletTypeNames.Length];
			for (int i=0; i<outletTypeNames.Length; i++)
			{
				Type type = Generator.GetGenericType(outletTypeNames[i]);
				Type genericType = typeof(Outlet<>).MakeGenericType(type);
				miss.outlets[i] = (IOutlet<object>)Activator.CreateInstance(genericType);
				miss.outlets[i].Id = outletIds[i];
				miss.outlets[i].SetGen(miss);
			}

			return miss;
		}


		public static void Restore (MissingGenPlug missingGen, Graph graph)
		{
			if (graph==null || !graph.ContainsGenerator(missingGen))
				throw new Exception("Currently opened graph does not contain generator to restore");
			
			bool restored = SerializedDataReader.TryCreateObjFromSerializedData(
				missingGen.assemblyName, missingGen.className, missingGen.namespaceName, missingGen.serializedData,
				out object restoredObj);

			if (!restored)
			{
				EditorUtility.DisplayDialog("Restore Failed", 
					"Could not restore from the serialized data:\n" + (string)restoredObj, "OK");
				return;
			}

			Generator restoredGen = (Generator)restoredObj;

			restoredGen.Id = missingGen.id;

			//inlets and outlets
			int i=0;
			foreach (IInlet<object> inlet in restoredGen.AllInlets())
			{
				if (GetGenericType(inlet) == GetGenericType(missingGen.inlets[i]))
					inlet.Id = missingGen.inlets[i].Id;
				i++;
			}

			int o=0;
			foreach (IOutlet<object> outlet in restoredGen.AllOutlets())
			{
				if (GetGenericType(outlet) == GetGenericType(missingGen.outlets[o]))
					outlet.Id = missingGen.outlets[o].Id;
				o++;
			}


			//graph
			graph.ReplaceGen(missingGen, restoredGen);





			//if (GraphWindow.current.graph.IsLinked(layer))
			//			GraphWindow.current.graph.UnlinkInlet(layer);
		}

		public override void Generate (TileData data, StopToken stop) { }

		#region Serialization

			[Serializable] private struct SerializedUnit { public ulong id; public string typeName; }
			[SerializeField] private SerializedUnit[] serializedInlets;
			[SerializeField] private SerializedUnit[] serializedOutlets;

			public override void OnBeforeSerialize ()
			{
				base.OnBeforeSerialize();

				if (serializedInlets == null || serializedInlets.Length != inlets.Length)
					serializedInlets = new SerializedUnit[inlets.Length];
				for (int i=0; i<serializedInlets.Length; i++)
					serializedInlets[i] = new SerializedUnit() { 
						id=inlets[i].Id, 
						typeName=inlets[i].GetType().GetGenericArguments()[0].AssemblyQualifiedName };

				if (serializedOutlets == null || serializedOutlets.Length != outlets.Length)
					serializedOutlets = new SerializedUnit[outlets.Length];
				for (int i = 0; i < serializedOutlets.Length; i++)
					serializedOutlets[i] = new SerializedUnit() { 
						id = outlets[i].Id, 
						typeName=outlets[i].GetType().GetGenericArguments()[0].AssemblyQualifiedName };
			}

			public override void OnAfterDeserialize ()
			{
				base.OnAfterDeserialize();

				inlets = new IInlet<object>[serializedInlets.Length];
				for (int i = 0; i < serializedInlets.Length; i++)
				{
					Type type = Type.GetType(serializedInlets[i].typeName);
					Type genericType = typeof(Inlet<>).MakeGenericType(type);
					inlets[i] = (IInlet<object>)Activator.CreateInstance(genericType);
					inlets[i].Id = serializedInlets[i].id;
					inlets[i].SetGen(this);
				}

				outlets = new IOutlet<object>[serializedOutlets.Length];
				for (int i = 0; i < serializedOutlets.Length; i++)
				{
					Type type = Type.GetType(serializedOutlets[i].typeName);
					Type genericType = typeof(Outlet<>).MakeGenericType(type);
					outlets[i] = (IOutlet<object>)Activator.CreateInstance(genericType);
					outlets[i].Id = serializedOutlets[i].id;
					outlets[i].SetGen(this);
				}
			}

		#endregion

		#region Testing

		public static void TestAdd (Graph graph)
		{
			string assemblyName = "MapMagic.Tests";
			string namespaceName = "";
			string className = "MyTempGenerator8";
			long referenceId = 1885265714747801614;
			string serializedData = @"enabled: 1
id: 9186461431510335489
version: 0
draftTime: 0
mainTime: 0
guiPosition: {x: 15.2, y: 104.8}
guiSize: {x: 150, y: 1068}
guiPreview: 0
guiAdvanced: 0
guiDebug: 0
serializedUnitIds: 0200000000de7c7f0300000000de7c7f0400000000de7c7f0500000000de7c7f
serializedMissingDownloadString: 
serializedMissingInletTypes:
- Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity
- Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity
- Den.Tools.TransitionsList, Den.Tools.Unity
serializedMissingOutletTypes:
- Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity
- Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity
- Den.Tools.TransitionsList, Den.Tools.Unity
serializedMissingInletIds: 0100000000de7c7f0200000000de7c7f0300000000de7c7f
serializedMissingOutletIds: 0100000000de7c7f0400000000de7c7f0500000000de7c7f
position:
  x: 0
  z: 0
radius: 30
hardness: 0.5
prefabval: {fileID: 0}
scriptableObjectval: {fileID: 0}
intval: 42
floatval: 42
doubleval: 42
vector2val: {x: 42, y: 42}
vector3val: {x: 42, y: 42, z: 42}
vector4val: {x: 42, y: 42, z: 42, w: 42}
rectval:
  serializedVersion: 2
  x: 0
  y: 0
  width: 42
  height: 42
intvector2val: {x: 42, y: 42}
intvector3val: {x: 42, y: 42, z: 42}
intvector4val: {x: 42, y: 42, z: 42, w: 42}
stringval: default
boolval: 1
longval: 42
shortval: 42
byteval: 42
charval: 99
uintval: 42
ulongval: 42
ushortval: 42
sbyteval: 42
coordval:
  x: 0
  z: 0
coordRectVal:
  offset:
    x: 0
    z: 0
  size:
    x: 0
    z: 0
vector2Dval:
  x: 0
  z: 0
colorval: {r: 0, g: 0, b: 0, a: 0}
enumVal: 0
rectOffsetVal:
  m_Left: 0
  m_Right: 0
  m_Top: 0
  m_Bottom: 0
intRectVal:
  x: 0
  y: 0
  width: 0
  height: 0
intArray: 0100000002000000030000000400000005000000
vectorArray:
- {x: 1, y: 2}
- {x: 3, y: 4}
coordArray:
- x: 1
  z: 2
- x: 3
  z: 4
emptyString: 
";

			string serializedDataBinary2text = @"enabled 1 (UInt8)
id 17709255445471821828 (UInt64)
<LinkedOutletId>k__BackingField 0 (UInt64)
<LinkedGenId>k__BackingField 0 (UInt64)
version 0 (UInt64)
draftTime 0 (double)
mainTime 0 (double)
guiPosition (0 96) (Vector2f)
guiSize (150 1068) (Vector2f)
guiPreview 0 (UInt8)
guiAdvanced 0 (UInt8)
guiDebug 0 (UInt8)
serializedMissingDownloadString """" (string)
serializedMissingInletTypes  (vector)
	size 3 (int)
	data ""Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"" (string)
	data ""Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"" (string)
	data ""Den.Tools.TransitionsList, Den.Tools.Unity, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"" (string)

serializedMissingOutletTypes  (vector)
	size 3 (int)
	data ""Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"" (string)
	data ""Den.Tools.Matrices.MatrixWorld, Den.Tools.Unity, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"" (string)
	data ""Den.Tools.TransitionsList, Den.Tools.Unity, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"" (string)

serializedMissingInletIds  (vector)
	size 3 (int)
	data (UInt64) #0: 17709255445471821828 17709255445471821829 17709255445471821830
serializedMissingOutletIds  (vector)
	size 3 (int)
	data (UInt64) #0: 17709255445471821828 17709257644495077383 17709257644495077384
position  (Vector2D)
	x 0 (float)
	z 0 (float)
radius 30 (float)
hardness 0.5 (float)
prefabval  (PPtr<$GameObject>)
	m_FileID 0 (int)
	m_PathID 0 (SInt64)
scriptableObjectval  (PPtr<$ScriptableObject>)
	m_FileID 0 (int)
	m_PathID 0 (SInt64)
intval 42 (int)
floatval 42 (float)
doubleval 42 (double)
vector2val (42 42) (Vector2f)
vector3val (42 42 42) (Vector3f)
vector4val (42 42 42 42) (Vector4f)
rectval  (Rectf)
	x 0 (float)
	y 0 (float)
	width 42 (float)
	height 42 (float)
intvector2val (42 42) (int2_storage)
intvector3val (42 42 42) (int3_storage)
intvector4val (42 42 42 42) (Vector4f)
stringval ""default"" (string)
boolval 1 (UInt8)
longval 42 (SInt64)
shortval 42 (SInt16)
byteval 42 (UInt8)
charval 99 (UInt16)
uintval 42 (unsigned int)
ulongval 42 (UInt64)
ushortval 42 (UInt16)
sbyteval 42 (SInt8)
coordval  (Coord)
	x 0 (int)
	z 0 (int)
coordRectVal  (CoordRect)
	offset  (Coord)
		x 0 (int)
		z 0 (int)
	size  (Coord)
		x 0 (int)
		z 0 (int)
vector2Dval  (Vector2D)
	x 0 (float)
	z 0 (float)
colorval (0 0 0 0) (ColorRGBA)
enumVal 0 (int)
rectOffsetVal (m_Left 0 m_Right 0 m_Top 0 m_Bottom 0) (RectOffset)
intRectVal  (RectInt)
	x 0 (int)
	y 0 (int)
	width 0 (int)
	height 0 (int)
intArray  (vector)
	size 5 (int)
	data (int) #0: 1 2 3 4 5
vectorArray  (vector)
	size 2 (int)
	data (1 2) (Vector2f)
	data (3 4) (Vector2f)

coordArray  (Coord)
	size 2 (int)
	data  (Coord)
		x 1 (int)
		z 2 (int)
	data  (Coord)
		x 3 (int)
		z 4 (int)

emptyString """" (string)
aIn  (MatrixInlet)
	id 17709255445471821829 (UInt64)
	<LinkedOutletId>k__BackingField 0 (UInt64)
	<LinkedGenId>k__BackingField 0 (UInt64)
bIn  (TransitionsInlet)
	id 17709255445471821830 (UInt64)
	<LinkedOutletId>k__BackingField 0 (UInt64)
	<LinkedGenId>k__BackingField 0 (UInt64)
aOut  (MatrixOutlet)
	id 17709257644495077383 (UInt64)
bOut  (TransitionsOutlet)
	id 17709257644495077384 (UInt64)";
	
			MissingGenPlug gen = CreatePlugFromData(assemblyName, namespaceName, className, referenceId, serializedDataBinary2text);
			graph.Add(gen);

		}

		#endregion
	}

	/*


	 */

}