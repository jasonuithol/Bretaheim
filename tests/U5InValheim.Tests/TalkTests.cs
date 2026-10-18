using System.Collections.Generic;
using System.Linq;
using Xunit;
using U5InValheim.Data;

namespace U5InValheim.Tests
{
    public class TalkTests
    {
        class World : ITalkWorld
        {
            public string AvatarName => "Shamino";
            public IEnumerable<string> PartyNames => new[] { "Shamino" };
            public bool Met { get; set; }
            public int Karma { get; set; } = 50;
            public int Gold { get; set; } = 100;
            public bool Heads = true;
            public List<int> Given = new List<int>();
            public void TakeGold(int amount) => Gold -= amount;
            public void Give(int item) => Given.Add(item);
            public bool Join() => false;
            public void Alarm() { }
            public bool Coin() => Heads;
        }

        static TalkSession Talk(int location, string name, World w)
        {
            var loc = Env.Data.Location(location);
            var npc = Env.Data.NpcsOf(loc).First(n => Env.Data.NpcName(loc, n) == name);
            return new TalkSession(Env.Data.ScriptOf(loc, npc), Env.Data.Ovl, w);
        }

        [U5Fact]
        public void JustinTellsHisNameAndJob()
        {
            var t = Talk(2, "Justin", new World());
            var hello = t.TakeOutput();
            Assert.StartsWith("You see", hello);
            Assert.Contains("I am called Justin", hello);
            Assert.True(t.Waiting);
            t.Respond("name");
            Assert.Contains("My name is Justin", t.TakeOutput());
            t.Respond("job");
            var job = t.TakeOutput();
            Assert.DoesNotContain("I cannot help", job);
            t.Respond("xyzzy");
            Assert.Contains("I cannot help thee with that", t.TakeOutput());
            t.Respond("bye");
            Assert.True(t.Over);
        }

        [U5Fact]
        public void EveryConversationRuns()
        {
            int talks = 0;
            foreach (var file in Env.Data.Talk)
            foreach (int n in file.Npcs)
            {
                var script = file[n];
                var keys = new[] { "name", "job", "" }.Concat(script.Keywords.SelectMany(k => k.Keys)).Concat(script.Labels.SelectMany(l => l.Keywords.SelectMany(k => k.Keys))).Distinct().ToList();
                foreach (var key in keys)
                foreach (var answer in new[] { "yes", "no" })
                {
                    var w = new World { Met = key.Length % 2 == 0, Heads = key.Length % 3 == 0 };
                    var t = new TalkSession(script, Env.Data.Ovl, w);
                    var replies = new Queue<string>(new[] { key, answer, answer, "Shamino", answer, "bye", "", "bye", "", "" });
                    int steps = 0;
                    while (!t.Over && steps++ < 40) t.Respond(replies.Count > 0 ? replies.Dequeue() : "bye");
                    // over, or still asking (quizzes repeat until answered, as in the original;
                    // the panel's Bye or walking away ends any conversation)
                    Assert.True(t.Over || t.Waiting, $"npc {n} key {key}: neither over nor waiting");
                    Assert.NotEmpty(t.TakeOutput());
                    talks++;
                }
            }
            Assert.True(talks > 500, $"{talks} conversations");
        }
    }
}
