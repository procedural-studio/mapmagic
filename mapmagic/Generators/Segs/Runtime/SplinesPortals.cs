﻿using System;
using System.Reflection;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using Den.Tools;
using MapMagic.Products;

namespace MapMagic.Nodes.SegsGenerators
{
	[GeneratorMenu (menu = "Spline/Portals", name = "Enter", iconName = "GeneratorIcons/PortalIn", lookLikePortal=true)] 
	public class SplinePortalEnter : PortalEnter<Den.Tools.Lines.LineSys> { } 

	[GeneratorMenu (menu = "Spline/Portals", name ="Exit", iconName = "GeneratorIcons/PortalOut", lookLikePortal=true)] 
	public class SplinePortalExit : PortalExit<Den.Tools.Lines.LineSys> { }
}