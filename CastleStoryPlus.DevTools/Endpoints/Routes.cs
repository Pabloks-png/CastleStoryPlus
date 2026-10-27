using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;

namespace CastleStoryPlus.DevTools.Endpoints;

// Every endpoint of the API (GET /api lists them with their parameters).
internal static class Routes
{
	internal static List<Route> Build()
	{
		List<Route> routes = new List<Route>();
		GameEndpoints.Add(routes);
		StorageEndpoints.Add(routes);
		BlueprintEndpoints.Add(routes);
		BuildSearchEndpoints.Add(routes);
		PlaceEndpoints.Add(routes);
		WorldEndpoints.Add(routes);
		ProjectEndpoints.Add(routes);
		CheatEndpoints.Add(routes);
		WorkshopEndpoints.Add(routes);
		ReachEndpoints.Add(routes);
		DemolishEndpoints.Add(routes);
		SelectionEndpoints.Add(routes);
		DiagnosticsEndpoints.Add(routes);
		PathEndpoints.Add(routes);
		PathStatsEndpoints.Add(routes);
		FrameEndpoints.Add(routes);
		FramePhaseEndpoints.Add(routes);
		HeapEndpoints.Add(routes);
		AllocEndpoints.Add(routes);
		SpeedEndpoints.Add(routes);
		QuitEndpoints.Add(routes);
		RenderEndpoints.Add(routes);
		DialogEndpoints.Add(routes);
		MenuEndpoints.Add(routes);
		TestEndpoints.Add(routes);
		TestWorldEndpoints.Add(routes);
		CameraEndpoints.Add(routes);
		RandomWorldEndpoints.Add(routes);
		TreasureEndpoints.Add(routes);
		SpawnEndpoints.Add(routes);
		VolumeDumpEndpoints.Add(routes);
		return routes;
	}
}
