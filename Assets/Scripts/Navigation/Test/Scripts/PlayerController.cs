using Navigation.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation.Test
{
    public class PlayerController : MonoBehaviour
    {
        public int Count = 5;
        public GameObject PlayerPrefab;
        public GameObject[] BirthPoints;

        private void Start()
        {
            // 根据 Count，在场景中随机生成 Player
            for (int i = 0; i < Count; i++)
            {
                var position = GenerateRandomDestination();
                GameObject go = Instantiate(PlayerPrefab, position, Quaternion.identity);
                var nav = go.GetComponent<NavigationAgent>();
                var playerColor = UnityEngine.Random.ColorHSV();
                go.transform.GetChild(0).gameObject.GetComponent<Renderer>().material.color = playerColor;
                nav.Destination = GenerateRandomDestination();
                nav.OnDestinationReached += OnDestinationReached;

                var player = go.GetComponent<Player>();
                player.PathColor = playerColor;
            }
        }

        private void OnDestinationReached(NavigationAgent agent)
        {
            // 当 Player 到达目的地时，重新设置目的地
            Debug.Log(agent.gameObject.name + "到达目的地");
            agent.Destination = GenerateRandomDestination();
        }

        public Vector3 GenerateRandomDestination()
        {
            // 随机生成一个目的地
            int index = UnityEngine.Random.Range(0, BirthPoints.Length);
            return BirthPoints[index].transform.position;
        }
    }
}