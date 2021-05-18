﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Adapters;
using Microsoft.Bot.Builder.Dialogs.Adaptive;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Actions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Input;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Recognizers;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Templates;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Bot.Builder.Dialogs.Declarative.Tests
{
    /// <summary>
    /// Test speech priming functionality.
    /// </summary>
    public class PrimingTests
    {
        private static AI.Luis.LuisAdaptiveRecognizer _luis = new ()
        {
            // The source would be generated by tooling and this is to distinguish the intent/entity across applications.
            // With package namespacing a component should name it with the package prefix, i.e. Microsoft.Welcome#foo.lu.
            Id = "leaf.lu",
            Recognizes = new[] { "intent1", "entity1", "dlist" },
            DynamicLists = new[]
            {
                    new AI.Luis.DynamicList()
                    {
                        Entity = "dlist",
                        List = new List<AI.LuisV3.ListElement>
                        {
                            new AI.LuisV3.ListElement("value1", new List<string> { "synonym1", "synonym2" })
                        }
                    }
            }
        };

        private static RecognitionHint[] _luisHints = new RecognitionHint[]
        {
            new LUReferenceHint("intent1", "leaf.lu"), new LUReferenceHint("entity1", "leaf.lu"), new LUReferenceHint("dlist", "leaf.lu"),
            new PhraseListHint("dlist", new[] { "value1", "synonym1", "synonym2" })
        };

        private static AI.Luis.LuisAdaptiveRecognizer _luisParent = new ()
        {
            // The source would be generated by tooling and this is to distinguish the intent/entity across applications.
            // With package namespacing a component should name it with the package prefix, i.e. Microsoft.Welcome#foo.lu.
            Id = "parent.lu",
            Recognizes = new[] { "intentParent", "entityParent", "dlistParent" },
            DynamicLists = new[]
            {
                    new AI.Luis.DynamicList()
                    {
                        Entity = "dlistParent",
                        List = new List<AI.LuisV3.ListElement>
                        {
                            new AI.LuisV3.ListElement("valueParent", new List<string> { "synonym1", "synonym2" })
                        }
                    }
            }
        };

        private static RecognitionHint[] _luisParentHints = new RecognitionHint[]
        {
            new LUReferenceHint("intentParent", "parent.lu"), new LUReferenceHint("entityParent", "parent.lu"), new LUReferenceHint("dlistParent", "parent.lu"),
            new PhraseListHint("dlistParent", new[] { "valueParent", "synonym1", "synonym2" })
        };

        private static AI.Luis.LuisAdaptiveRecognizer _luisEN = new ()
        {
            // The source would be generated by tooling and this is to distinguish the intent/entity across applications.
            // With package namespacing a component should name it with the package prefix, i.e. Microsoft.Welcome#foo.lu.
            Id = "leaf.en-us.lu",
            Recognizes = new[] { "intent1", "entity1", "dlist" },
            DynamicLists = new[]
            {
                    new AI.Luis.DynamicList()
                    {
                        Entity = "dlist",
                        List = new List<AI.LuisV3.ListElement>
                        {
                            new AI.LuisV3.ListElement("value1", new List<string> { "synonym1", "synonym2" })
                        }
                    }
            }
        };

        private static RecognitionHint[] _luisENHints = new RecognitionHint[]
        {
            new LUReferenceHint("intent1", "leaf.en-us.lu"), new LUReferenceHint("entity1", "leaf.en-us.lu"), new LUReferenceHint("dlist", "leaf.en-us.lu"),
            new PhraseListHint("dlist", new[] { "value1", "synonym1", "synonym2" })
        };

        private static AI.QnA.Recognizers.QnAMakerRecognizer _qna = new ();

        private static MultiLanguageRecognizer _multi = new () { Recognizers = new Dictionary<string, Recognizer> { { "en-us", _luisEN }, { string.Empty, _luis } } };

        private static ActivityTemplate _prompt = new ("Prompt");

        private static JObject _schema = JObject.Parse(@"
            {
                'type': 'object',
                'properties': {
                    'property1': {
                        'type': 'string',
                        '$entities': ['entity1']
                    }
                }
            }");

        private static AdaptiveDialog _leaf = new ()
        {
            Id = "leaf",
            Recognizer = _luis,
            Triggers = new List<OnCondition>
                        {
                            new OnBeginDialog(new List<Dialog>
                            {
                                new Ask()
                                {
                                    Activity = _prompt,
                                    ExpectedProperties = new[] { "property1" }
                                }
                            })
                        },
            Schema = _schema
        };

        private static AdaptiveDialog _parent = new ()
        {
            Recognizer = _luisParent,
            Triggers = new List<OnCondition>
            {
                new OnBeginDialog(new List<Dialog>
                {
                    new BeginDialog("leaf")
                })
            }
        };

        private static AdaptiveDialog _withInterruptions = new ()
        {
            Recognizer = _luisParent,
            Triggers = new List<OnCondition>
            {
                new OnBeginDialog(new List<Dialog> { new NumberInput { Prompt = _prompt, AllowInterruptions = true } })
            }
        };

        private static AdaptiveDialog _withoutInterruptions = new ()
        {
            Recognizer = _luisParent,
            Triggers = new List<OnCondition>
            {
                new OnBeginDialog(new List<Dialog> { new NumberInput { Prompt = _prompt, AllowInterruptions = false } })
            }
        };

        public static IEnumerable<object[]> HintExamples
            => new[]
            {
                new object[] { new PreBuiltHint("prebuilt") },
                new object[] { new LUReferenceHint("intent", "foo.lu") },
                new object[] { new RegexHint("regex", "a*") },
                new object[] { new PhraseListHint("phraseList", new[] { "a b", "c d" }) },
            };

        public static IEnumerable<object[]> ExpectedRecognizer
            => new[]
            {
                // Entity recognizers
                new object[] { new AgeEntityRecognizer(), new[] { new PreBuiltHint("age") } },
                new object[] { new ChannelMentionEntityRecognizer(), new[] { new PreBuiltHint("channelMention") } },
                new object[] { new ConfirmationEntityRecognizer(), new[] { new PreBuiltHint("boolean") } },
                new object[] { new CurrencyEntityRecognizer(), new[] { new PreBuiltHint("currency") } },
                new object[] { new DateTimeEntityRecognizer(), new[] { new PreBuiltHint("datetime") } },
                new object[] { new DimensionEntityRecognizer(), new[] { new PreBuiltHint("dimension") } },
                new object[] { new EmailEntityRecognizer(), new[] { new PreBuiltHint("email") } },
                new object[] { new GuidEntityRecognizer(), new[] { new PreBuiltHint("guid") } },
                new object[] { new HashtagEntityRecognizer(), new[] { new PreBuiltHint("hashtag") } },
                new object[] { new IpEntityRecognizer(), new[] { new PreBuiltHint("ip") } },
                new object[] { new MentionEntityRecognizer(), new[] { new PreBuiltHint("mention") } },
                new object[] { new NumberEntityRecognizer(), new[] { new PreBuiltHint("number") } },
                new object[] { new NumberRangeEntityRecognizer(), new[] { new PreBuiltHint("numberrange") } },
                new object[] { new OrdinalEntityRecognizer(), new[] { new PreBuiltHint("ordinal") } },
                new object[] { new PercentageEntityRecognizer(), new[] { new PreBuiltHint("percentage") } },
                new object[] { new PhoneNumberEntityRecognizer(), new[] { new PreBuiltHint("phonenumber") } },
                new object[] { new RegexEntityRecognizer() { Name = "pattern", Pattern = "a*" }, new[] { new RegexHint("pattern", "a*") } },
                new object[] { new TemperatureEntityRecognizer(), new[] { new PreBuiltHint("temperature") } },
                new object[] { new UrlEntityRecognizer(), new[] { new PreBuiltHint("url") } },

                // Regex Recognizer
                new object[]
                {
                    new RegexRecognizer
                    {
                        Intents = new List<IntentPattern> { new IntentPattern("regexIntent", "intent*") },
                        Entities = new List<EntityRecognizer> { new NumberEntityRecognizer(), new RegexEntityRecognizer { Name = "regexEntity", Pattern = "entity*" } }
                    },
                    new RecognitionHint[] { new RegexHint("regexIntent", "intent*"), new RegexHint("regexEntity", "entity*"), new PreBuiltHint("number") }
                },

                // LUIS
                new object[] { _luis, _luisHints },

                // QnA doesn't have any priming information
                new object[] { _qna, null },

                // Recognizer set
                new object[]
                {
                    new RecognizerSet() { Recognizers = new List<Recognizer> { new NumberEntityRecognizer(), _luis } },
                    _luisHints.Union(new RecognitionHint[] { new PreBuiltHint("number") })
                },

                // Cross-trained recognizer
                new object[]
                {
                    new CrossTrainedRecognizerSet() { Recognizers = new List<Recognizer> { _luis, new NumberEntityRecognizer() } },
                    _luisHints.Union(new RecognitionHint[] { new PreBuiltHint("number") })
                },

                // Multi language recognizer
                new object[] { _multi, _luisHints, "de-de" },

                new object[] { _multi, _luisENHints, "en-us" },
            };

        public static IEnumerable<object[]> ExpectedDialog
            => new[]
            {
                new object[] { new AttachmentInput() { Prompt = _prompt } },
                new object[] { new ConfirmInput() { Prompt = _prompt }, new[] { new PreBuiltHint("boolean") } },
                new object[] { new DateTimeInput() { Prompt = _prompt }, new[] { new PreBuiltHint("datetimeV2") } },
                new object[] { new NumberInput() { Prompt = _prompt }, new[] { new PreBuiltHint("number") } },
                new object[] { new TextInput() { Prompt = _prompt } },
                new object[]
                {
                    new ChoiceInput()
                    {
                        Id = "choiceTest",
                        Prompt = _prompt,
                        Choices = new ChoiceSet(new[]
                        {
                            new Choice("value1")
                            {
                                Action = new CardAction(title: "Action"),
                                Synonyms = new List<string> { "synonym1", "synonym2" }
                            }
                        })
                    },
                    new RecognitionHint[] 
                    { 
                        new PreBuiltHint("number"), new PreBuiltHint("ordinal"), 
                        new PhraseListHint("choiceTest", new[] { "value1", "Action", "synonym1", "synonym2" })
                    }
                },
                new object[]
                {
                    new ChoiceInput()
                    {
                        Id = "choiceTest",
                        Prompt = _prompt,
                        Choices = new ChoiceSet(new[]
                        {
                            new Choice("value1")
                            {
                                Action = new CardAction(title: "Action"),
                                Synonyms = new List<string> { "synonym1", "synonym2" }
                            }
                        }),
                        RecognizerOptions = new FindChoicesOptions() { NoAction = true, NoValue = true, RecognizeNumbers = false, RecognizeOrdinals = false }
                    },
                    new RecognitionHint[]
                    {
                        new PhraseListHint("choiceTest", new[] { "synonym1", "synonym2" })
                    }
                },
                new object[] 
                { 
                    _leaf, _luisHints,
                    new RecognitionHint[] { new LUReferenceHint("entity1", "leaf.lu") },
                    _luisHints.Where(h => h.Name != "entity1")
                },
                new object[]
                {
                    _parent, _luisParentHints,
                    new RecognitionHint[] { new LUReferenceHint("entity1", "leaf.lu") },
                    _luisHints.Where(h => h.Name != "entity1").Union(_luisParentHints)
                },
                new object[]
                {
                    _withInterruptions,
                    _luisParentHints,
                    new[] { new PreBuiltHint("number") },
                    _luisParentHints
                },
                new object[]
                {
                    _withoutInterruptions,
                    _luisParentHints,
                    new[] { new PreBuiltHint("number") }
                },
            };

        [Theory]
        [MemberData(nameof(HintExamples))]
        public void HintSerialization(RecognitionHint hint)
        {
            var copiedHint = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(hint)) as JObject;
            var props = hint.GetType().GetProperties();
            foreach (var prop in props)
            {
                var original = prop.GetValue(hint);
                var name = (prop.GetCustomAttributes(typeof(JsonPropertyAttribute), true)[0] as JsonPropertyAttribute).PropertyName;
                var copyValue = copiedHint[name];
                if (copyValue is JValue jv)
                {
                    Assert.Equal(original, jv.Value);
                }
                else if (copyValue is JArray ja)
                {
                    Assert.Equal(original, ja.ToObject<List<string>>());
                }
            }
        }

        [Theory]
        [MemberData(nameof(ExpectedRecognizer))]
        public void RecognizerHints(Recognizer recognizer, RecognitionHint[] hints, string locale = null)
        {
            CheckHints(recognizer.GetRecognitionHints(GetTurnContext(locale: locale)), hints);
        }

        [Theory]
        [MemberData(nameof(ExpectedDialog))]
        public async Task DialogHints(Dialog dialog, RecognitionHint[] dialogHints = null, RecognitionHint[] expectedHints = null, RecognitionHint[] possibleHints = null, string locale = null)
        {
            if (dialogHints == null)
            {
                dialogHints = Array.Empty<RecognitionHint>();
            }

            expectedHints = AssignImportance(expectedHints ?? dialogHints, RecognitionHintImportance.Expected);
            possibleHints = AssignImportance(possibleHints ?? Array.Empty<RecognitionHint>(), RecognitionHintImportance.Possible);
            var dc = GetTurnContext(dialog, locale: locale);
            await dc.BeginDialogAsync(dialog.Id);
            CheckHints(dialog.GetRecognitionHints(dc), dialogHints);
            var hints = RecognitionHints(dc);
            CheckHints(hints, expectedHints.Union(possibleHints).ToArray());
        }

        private RecognitionHint[] AssignImportance(IEnumerable<RecognitionHint> hints, RecognitionHintImportance importance)
        {
            var newHints = new List<RecognitionHint>();
            foreach (var hint in hints)
            {
                var newHint = hint.Clone();
                newHint.Importance = importance.ToString();
                newHints.Add(newHint);
            }

            return newHints.ToArray();
        }

        private void CheckHints(IEnumerable<RecognitionHint> actual, RecognitionHint[] expected)
        {
            var actualHints = actual.ToArray<RecognitionHint>();
            if (expected == null)
            {
                Assert.Equal(Array.Empty<RecognitionHint>(), actualHints);
            }
            else
            {
                Assert.Equal(expected.Length, actualHints.Length);
                foreach (var hint in actualHints)
                {
                    var match = expected.FirstOrDefault(h => h.Name == hint.Name && h.Type == hint.Type);
                    Assert.NotNull(match);
                    var hintString = JsonConvert.SerializeObject(hint);
                    var matchString = JsonConvert.SerializeObject(match);
                    Assert.Equal(matchString, hintString);
                }
            }
        }

        private List<RecognitionHint> RecognitionHints(DialogContext dc)
            => ((dc.Services.Get<ITurnContext>().Adapter as TestAdapter).ActiveQueue.First(q => q.Type == ActivityTypes.Command)?.Value as CommandValue<List<RecognitionHint>>)?.Data;

        private DialogContext GetTurnContext(Dialog dialog = null, string locale = null)
        {
            var dialogs = new DialogSet();

            // Explicitly add leaf since it is nested in parent
            dialogs.Add(_leaf);
            if (dialog != null)
            {
                dialogs.Add(dialog);
            }

            var turn = new TurnContext(
                    new TestAdapter(TestAdapter.CreateConversation("Priming")),
                    new Activity() { Locale = locale ?? "en-us" });
            var dc = new DialogContext(
                dialogs,
                turn,
                new DialogState());
            dc.Services.Add<ITurnContext>(turn);
            return dc;
        }
    }
}
