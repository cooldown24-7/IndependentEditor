using System.Text.Json.Nodes;
namespace IndependentDev;

internal static class EventTimeline
{
    public static void Validate(Scene scene,JsonObject? animation)
    {
        var definitions=Scene.Object(scene.Data["events"]);
        void Values(JsonObject values)
        {
            if(values["int"] is not null)Scene.Integer(values["int"]);
            foreach(var field in new[]{"float","volume","balance"})if(values[field] is not null)Scene.Number(values[field]);
            foreach(var field in new[]{"string","audio"})if(values[field] is not null)Scene.Text(values[field]);
        }
        foreach(var entry in definitions??[])
        {if(entry.Key.Length==0)throw new InvalidDataException("Event name");Values(Scene.Object(entry.Value)??throw new InvalidDataException("Event definition object"));}
        var keys=Scene.Array(animation?["events"]);double previous=-1;
        foreach(var node in keys??[])
        {
            var frame=Scene.Object(node)??throw new InvalidDataException("Event frame object");
            var time=Scene.Number(frame["time"]);if(time<0||time<previous)throw new InvalidDataException("Event timeline order");previous=time;
            var name=Scene.Text(frame["name"]);if(name.Length==0||definitions?[name] is null)throw new InvalidDataException("Event reference");Values(frame);
        }
        // Selected-time posing validates and preserves event data; it does not dispatch audio or application callbacks.
    }
}
