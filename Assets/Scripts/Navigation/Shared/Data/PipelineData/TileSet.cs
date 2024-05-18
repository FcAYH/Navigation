using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Navigation.PipelineData
{
    public class TileSet
    {
        public Tile[] Tiles { get; set; }
    }

    [JsonConverter(typeof(TileConverter))]
    public struct Tile
    {
        public int Id { get; private set; }
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }

        public float Width => Max.x - Min.x;
        public float Depth => Max.z - Min.z;
        public float Height => Max.y - Min.y;

        public Tile(int id, Vector3 min, Vector3 max)
        {
            Id = id;
            Min = min;
            Max = max;
        }
    }

    public class TileConverter : JsonConverter<Tile>
    {
        public override Tile ReadJson(JsonReader reader, Type objectType, Tile existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jObject = JObject.Load(reader);
            int id = (int)jObject["Id"];
            JsonVector3 min = (JsonVector3)jObject["Min"];
            JsonVector3 max = (JsonVector3)jObject["Max"];
            return new Tile(id, min, max);
        }

        public override void WriteJson(JsonWriter writer, Tile value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Id");
            writer.WriteValue(value.Id);
            writer.WritePropertyName("Min");
            JsonVector3 vector3Min = value.Min;
            serializer.Serialize(writer, vector3Min);
            writer.WritePropertyName("Max");
            JsonVector3 vector3Max = value.Max;
            serializer.Serialize(writer, vector3Max);
            writer.WriteEndObject();
        }

        [Serializable]
        public struct JsonVector3
        {
            public float x;
            public float y;
            public float z;

            public static implicit operator Vector3(JsonVector3 jsonVector3)
            {
                return new Vector3(jsonVector3.x, jsonVector3.y, jsonVector3.z);
            }

            public static implicit operator JsonVector3(Vector3 vector3)
            {
                return new JsonVector3
                {
                    x = vector3.x,
                    y = vector3.y,
                    z = vector3.z
                };
            }

            public static explicit operator JsonVector3(JToken v)
            {
                return new JsonVector3
                {
                    x = (float)v["x"],
                    y = (float)v["y"],
                    z = (float)v["z"]
                };
            }
        }
    }
}