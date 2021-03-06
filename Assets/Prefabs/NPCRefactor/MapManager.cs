﻿/**
 * Copyright (c) 2018 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum TrafficLightSetState
{
    Red,
    Green,
    Yellow,
    None
};

public class MapManager : MonoBehaviour
{
    #region Singleton
    private static MapManager _instance = null;
    public static MapManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = GameObject.FindObjectOfType<MapManager>();
                if (_instance == null)
                    Debug.LogError("<color=red>MapManager Not Found!</color>");
            }
            return _instance;
        }
    }
    #endregion

    #region vars
    public Transform spawnLanesHolder;
    public Transform intersectionsHolder;

    private MapLaneSegmentBuilder tempLane;
    [System.NonSerialized]
    public List<MapSegmentBuilder> segBldrs = new List<MapSegmentBuilder>();
    [System.NonSerialized]
    public List<MapLaneSegmentBuilder> laneBldrs = new List<MapLaneSegmentBuilder>();
    [System.NonSerialized]
    public List<MapLaneSegmentBuilder> spawnLaneBldrs = new List<MapLaneSegmentBuilder>();
    [System.NonSerialized]
    public List<MapStopLineSegmentBuilder> stopLines = new List<MapStopLineSegmentBuilder>();
    private float connectionProximity = 1.0f;

    // lights
    [System.NonSerialized]
    public List<IntersectionComponent> intersections = new List<IntersectionComponent>();
    public Material green;
    public Material yellow;
    public Material red;
    private float yellowTime = 3f;
    private float allRedTime = 1.5f;
    private float activeTime = 15f;


    [HideInInspector]
    public bool isInit = false;
    #endregion

    #region mono
    private void Awake()
    {
        if (_instance == null)
            _instance = this;

        if (_instance != this)
            DestroyImmediate(gameObject);
    }

    private void Start()
    {
        GetMapData();
        ProcessLaneData();
        ProcessSpawnLaneData();
        ProcessStopLineData();
        ProcessTrafficLightData();
        ProcessIntersectionData();
        isInit = true;

        InitTrafficSets();
        EnableTrafficLightCycle();
    }
    #endregion

    private void GetMapData()
    {
        segBldrs.AddRange(transform.GetComponentsInChildren<MapSegmentBuilder>());
        laneBldrs.AddRange(transform.GetComponentsInChildren<MapLaneSegmentBuilder>());
        stopLines.AddRange(transform.GetComponentsInChildren<MapStopLineSegmentBuilder>());
        intersections.AddRange(intersectionsHolder.GetComponentsInChildren<IntersectionComponent>());
    }

    private void ProcessLaneData()
    {
        foreach (var segment in laneBldrs)
        {
            segment.segment.builder = segment; // ref
            foreach (var localPos in segment.segment.targetLocalPositions) // convert target world pos
                segment.segment.targetWorldPositions.Add(segment.segment.builder.transform.TransformPoint(localPos)); //Convert to world position
        }
        SetLaneConnections();
    }

    private void ProcessSpawnLaneData()
    {
        foreach (Transform child in spawnLanesHolder)
        {
            tempLane = child.GetComponent<MapLaneSegmentBuilder>();
            if (tempLane != null)
                spawnLaneBldrs.Add(tempLane);
        }
    }

    private void ProcessStopLineData()
    {
        foreach (var segment in stopLines)
        {
            segment.segment.builder = segment;
            foreach (var localPos in segment.segment.targetLocalPositions)
                segment.segment.targetWorldPositions.Add(segment.segment.builder.transform.TransformPoint(localPos));
        }
        SetLaneStopLines();
    }

    private void ProcessTrafficLightData()
    {
        foreach (var item in intersections)
        {
            item.SetLightGroupData(yellowTime, allRedTime, activeTime, yellow, red, green);
            foreach (var set in item.lightGroups)
            {
                set.SetLightRendererData();
            }
        }
    }
    
    private void ProcessIntersectionData()
    {
        foreach (var segment in stopLines)
        {
            segment.mapIntersectionBuilder = segment.GetComponentInParent<MapIntersectionBuilder>(); // get map intersection builder for this stopline
            if (segment.mapIntersectionBuilder != null)
            {
                segment.mapIntersectionBuilder.GetIntersection();
                segment.GetTrafficLightSet();
                segment.mapIntersectionBuilder.isStopSign = segment.isStopSign;
            }
        }
    }

    private void InitTrafficSets()
    {
        foreach (var item in intersections)
        {
            foreach (var set in item.lightGroups)
            {
                set.SetLightColor(TrafficLightSetState.Red, red);
            }
        }
    }

    private void EnableTrafficLightCycle()
    {
        foreach (var item in intersections)
        {
            item.StartTrafficLightLoop();
        }
    }
    
    private void SetLaneConnections()
    {
        foreach (var segment in laneBldrs)
        {
            var lastPt = segment.segment.builder.transform.TransformPoint(segment.segment.targetLocalPositions[segment.segment.targetLocalPositions.Count - 1]);

            foreach (var segment_cmp in laneBldrs)
            {
                var firstPt_cmp = segment_cmp.segment.builder.transform.TransformPoint(segment_cmp.segment.targetLocalPositions[0]);

                if ((lastPt - firstPt_cmp).magnitude < connectionProximity)
                {
                    segment.nextConnectedLanes.Add(segment_cmp);
                }
            }
        }
    }

    private void SetLaneStopLines()
    {
        foreach (var segment in stopLines)
        {
            List<Vector2> stopline2D = segment.segment.targetWorldPositions.Select(p => new Vector2(p.x, p.z)).ToList();
            
            foreach (var laneSeg in laneBldrs)
            {
                // check if any points intersect with segment
                List<Vector2> intersects = new List<Vector2>();
                var lanes2D = laneSeg.segment.targetWorldPositions.Select(p => new Vector2(p.x, p.z)).ToList();
                var lane2D = new List<Vector2>();
                lane2D.Add(lanes2D[lanes2D.Count-1]);
                bool isIntersected = Utils.CurveSegmentsIntersect(stopline2D, lane2D, out intersects);
                bool isClose = IsPointCloseToLine(stopline2D[0], stopline2D[stopline2D.Count-1], lanes2D[lanes2D.Count - 1]);
                if (isIntersected || isClose)
                    laneSeg.stopLine = segment;
            }
        }
    }

    public MapLaneSegmentBuilder GetRandomLane(Transform target = null)
    {
        return spawnLaneBldrs[(int)Random.Range(0, spawnLaneBldrs.Count)];
    }

    private bool IsPointCloseToLine(Vector2 p1, Vector2 p2, Vector2 pt)
    {
        bool isClose = false;
        Vector2 closestPt = Vector2.zero;
        float dx = p2.x - p1.x;
        float dy = p2.y - p1.y;

        // Calculate the t that minimizes the distance.
        float t = ((pt.x - p1.x) * dx + (pt.y - p1.y) * dy) / (dx * dx + dy * dy);

        // See if this represents one of the segment's
        // end points or a point in the middle.
        if (t < 0)
        {
            closestPt = new Vector2(p1.x, p1.y);
            dx = pt.x - p1.x;
            dy = pt.y - p1.y;
        }
        else if (t > 1)
        {
            closestPt = new Vector2(p2.x, p2.y);
            dx = pt.x - p2.x;
            dy = pt.y - p2.y;
        }
        else
        {
            closestPt = new Vector2(p1.x + t * dx, p1.y + t * dy);
            dx = pt.x - closestPt.x;
            dy = pt.y - closestPt.y;
        }

        if (Mathf.Sqrt(dx * dx + dy * dy) < connectionProximity)
        {
            isClose = true;
        }
        return isClose;
    }
}

