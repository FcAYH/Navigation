using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Navigation.PreferenceData
{
    [CreateAssetMenu(menuName = "Navigation/Generator/Agents")]
    public class Agents : ScriptableObject
    {
        [ReadOnly]
        public List<Agent> AgentList;

        public Agents()
        {
            AgentList = new List<Agent>();

            Agent defaultAgent = new Agent();
            defaultAgent.Name = "Human";
            defaultAgent.Height = 2.0f;
            defaultAgent.Radius = 0.4f;
            defaultAgent.MaxSlope = 45;
            defaultAgent.MaxStepHeight = 0.7f;

            AgentList.Add(defaultAgent);
        }
    }
}