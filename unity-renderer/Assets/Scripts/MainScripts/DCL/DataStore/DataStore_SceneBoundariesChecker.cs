using System.Collections.Generic;
using DCL.Controllers;
using DCL.ECSRuntime;
using DCL.Models;


namespace DCL
{
    public class DataStore_SceneBoundariesChecker
    {
        public BaseDictionary<long, List<IOutOfSceneBoundariesHandler>> componentsCheckSceneBoundaries = new BaseDictionary<long, List<IOutOfSceneBoundariesHandler>>();
    }
}