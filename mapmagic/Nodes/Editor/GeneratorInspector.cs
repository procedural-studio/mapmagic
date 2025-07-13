using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Den.Tools;
using Den.Tools.GUI;

using UnityEditor;



namespace MapMagic.Nodes.GUI
{
	public class GeneratorInspector : EditorWindow
	{
		UI ui = new UI();
		public float scroll; //for the scroll position of sub-menu
		public static Dictionary<Generator,bool> folded = new Dictionary<Generator,bool>();

		public void OnGUI () => ui.Draw(DrawGUI, inInspector:false);
		
		public void DrawGUI ()
		{
			using (Cell.Padded(5))
				using (Cell.LinePx(0))
				//using (new Draw.ScrollGroup(ref scroll)) //no scrolling until new Cell.Scrolled is implemented
				{
					foreach (Generator gen in GraphWindow.lastSelected)
					{
						DrawGenerator(gen);
						Cell.EmptyLinePx(10);
					}
				}
		}


		/*public static void ShowWindow ()
		{
			GeneratorInspector window = new GeneratorInspector();
			window.position = new Rect(Screen.currentResolution.width/2-100, Screen.currentResolution.height/2-50, 200, 100);
			window.ShowAuxWindow();
		}*/


		public static void DrawGenerator (Generator gen)
		{
			Type genType = gen.GetType();
			GeneratorMenuAttribute menuAtt = GeneratorDraw.GetMenuAttribute(genType);

			if (!folded.TryGetValue(gen, out bool genFolded))
			{
				folded.Add(gen, true);
				genFolded = true;
			}

			using (Cell.LinePx(30))
				using (new Draw.FoldoutGroup(ref genFolded, isLeft:true, headerContents:()=>DrawHeader(gen, menuAtt)))
				{
					folded[gen] = genFolded;
					if (genFolded)
					{
						using (Cell.LinePx(0))
							DrawFields(gen, menuAtt);
					}
				}
		}


		public static void DrawHeader (Generator gen, GeneratorMenuAttribute menuAtt)
		{
			Cell.EmptyLinePx(5);
			using (Cell.LinePx(20))
			{
				Cell.EmptyRowPx(10);

				//icon
				using (Cell.RowPx(24)) Draw.Icon(menuAtt.icon, scale:0.5f);

				//label
				bool nameFit = true;
				using (Cell.Row) 
				{
					Draw.Label(menuAtt.nameUpper, style:UI.current.styles.bigLabel);

					if (!UI.current.layout  &&  menuAtt.nameWidth > Cell.current.InternalRect.width)
						nameFit = false;
				}

				using (Cell.RowPx(25)) 
				{
					if (!nameFit)
						Draw.Label("...", style:UI.current.styles.bigLabel);
				}
			}
			Cell.EmptyLinePx(5);
		}


		public static void DrawFields (Generator gen, GeneratorMenuAttribute menuAtt)
		{
			using (Cell.LinePx(0))
			{
				Cell.current.fieldWidth = 0.44f;

				using (Cell.Row) 
					GeneratorDraw.DrawField(gen, menuAtt.advancedOptions);
			}

			using(ProfilerExt.Profile("Custom Editor"))
				using (Cell.LinePx(0))
					Draw.Editor(gen);

			if (Cell.current.valChanged)
				using(ProfilerExt.Profile("ValCahnged"))
			{
				gen.version ++;

				#if MM_DEBUG
				Log.Add("DrawGenerator ValueChanged", idName:null, ("gen ver:",gen.version));
				#endif

				GraphWindow.current?.RefreshMapMagic();
			}

			if (!UI.current.layout)
				gen.guiFieldHeight = Cell.current.InternalRect.height;
		}


		[MenuItem ("Window/MapMagic/Node Inspector")]
		public static void ShowEditor () => ShowEditor( new Rect(100,100,300,200) );

		public static void ShowEditor (Rect position)
		{
			GeneratorInspector  window = GetWindow<GeneratorInspector>("Node Inspector");

			Texture2D icon = TexturesCache.LoadTextureAtPath("MapMagic/Icons/Window");
			window.titleContent = new GUIContent("MapMagic Node", icon);

			window.position = position;
		}


		private static void DockWindowToLeft (EditorWindow window, EditorWindow dockToWindow)
		//Sometimes work, but sometimes ruins all the layout
		{
			Type dockAreaType = Type.GetType("UnityEditor.DockArea, UnityEditor");
			if (dockAreaType == null)
			{
				Debug.LogError("DockArea type not found.");
				return;
			}

			FieldInfo panesField = dockAreaType.GetField("m_Panes", BindingFlags.Instance | BindingFlags.NonPublic);
			if (panesField == null)
			{
				Debug.LogError("m_Panes field not found.");
				return;
			}

			 object dockArea = dockToWindow.GetType().BaseType.GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dockToWindow);

			IList panes = (IList)panesField.GetValue(dockArea);
			if (panes != null)
			{
				panes.Add(window); // Insert the window at the beginning to dock it to the left
				return;
			}
		}

	}
}