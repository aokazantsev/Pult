using System;
using System.Collections.Generic;
using System.Text;

namespace Pult
{
    internal static class ChoiceAnswers
    {
        public static string Validate(Dictionary<string, object> request, Dictionary<string, object> response)
        {
            if (!Json.Bool(request, "answerable")) return "not answerable";
            var questions = new Dictionary<string, Dictionary<string, object>>();
            foreach (object item in Items(Json.Value(request, "questions"))) questions[Json.String((Dictionary<string, object>)item, "id")] = (Dictionary<string, object>)item;
            var answered = new HashSet<string>();
            foreach (object item in Items(Json.Value(response, "answers")))
            {
                var answer = (Dictionary<string, object>)item;
                string questionId = Json.String(answer, "question");
                Dictionary<string, object> question;
                if (!questions.TryGetValue(questionId, out question) || !answered.Add(questionId)) return "unknown or repeated question";
                var options = new HashSet<string>();
                foreach (object option in Items(Json.Value(question, "options"))) options.Add(Json.String((Dictionary<string, object>)option, "id"));
                var selected = new List<string>();
                foreach (object id in Items(Json.Value(answer, "selected"))) selected.Add((string)id);
                if (new HashSet<string>(selected).Count != selected.Count) return "bad selection";
                foreach (string id in selected)
                {
                    if (!options.Contains(id)) return "bad selection";
                }
                string other = Json.String(answer, "other");
                bool hasOther = answer.ContainsKey("other");
                if (hasOther && (other == null || !Json.Bool(question, "other") || MessageRules.IsBlank(other))) return "bad other";
                if (Json.Bool(question, "multi"))
                {
                    if (selected.Count == 0 && !hasOther) return "empty multi answer";
                }
                else if (!((selected.Count == 1 && !hasOther) || (selected.Count == 0 && hasOther)))
                {
                    return "single answer must be one option or other";
                }
            }
            if (answered.Count != questions.Count) return "not every question answered";
            return null;
        }

        public static Dictionary<string, object> UpdatedInput(Dictionary<string, object> toolInput, Dictionary<string, object> request, Dictionary<string, object> response)
        {
            var updated = new Dictionary<string, object>(toolInput);
            var answers = new Dictionary<string, object>();
            var byQuestion = new Dictionary<string, Dictionary<string, object>>();
            foreach (object item in Items(Json.Value(response, "answers"))) byQuestion[Json.String((Dictionary<string, object>)item, "question")] = (Dictionary<string, object>)item;
            foreach (object item in Items(Json.Value(request, "questions")))
            {
                var question = (Dictionary<string, object>)item;
                Dictionary<string, object> answer = byQuestion[Json.String(question, "id")];
                var labels = new Dictionary<string, string>();
                foreach (object option in Items(Json.Value(question, "options"))) labels[Json.String((Dictionary<string, object>)option, "id")] = Json.String((Dictionary<string, object>)option, "label");
                var parts = new List<string>();
                foreach (object id in Items(Json.Value(answer, "selected"))) parts.Add(labels[(string)id]);
                string other = Json.String(answer, "other");
                if (other != null) parts.Add(other.Trim());
                answers[Json.String(question, "question")] = string.Join(", ", parts.ToArray());
            }
            updated["answers"] = answers;
            return updated;
        }

        private static IEnumerable<object> Items(object value)
        {
            var array = value as object[];
            if (array != null) return array;
            var list = value as System.Collections.IList;
            var result = new List<object>();
            if (list != null)
            {
                foreach (object item in list) result.Add(item);
            }
            return result;
        }
    }
}
