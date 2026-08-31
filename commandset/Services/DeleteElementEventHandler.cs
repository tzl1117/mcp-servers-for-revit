using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DeleteElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }

        public int DeletedCount { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        public string[] ElementIds { get; set; }
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }
        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                DeletedCount = 0;
                if (ElementIds == null || ElementIds.Length == 0)
                {
                    IsSuccess = false;
                    return;
                }
                List<ElementId> elementIdsToDelete = new List<ElementId>();
                List<string> invalidIds = new List<string>();
                foreach (var idStr in ElementIds)
                {
                    if (int.TryParse(idStr, out int elementIdValue))
                    {
                        var elementId = new ElementId(elementIdValue);
                        if (doc.GetElement(elementId) != null)
                        {
                            elementIdsToDelete.Add(elementId);
                        }
                    }
                    else
                    {
                        invalidIds.Add(idStr);
                    }
                }
                if (invalidIds.Count > 0)
                {
                    TaskDialog.Show("警告", $"以下ID无效或元素不存在：{string.Join(", ", invalidIds)}");
                }
                if (elementIdsToDelete.Count > 0)
                {
                    using (var transaction = new Transaction(doc, "Delete Elements"))
                    {
                        transaction.Start();

                        ICollection<ElementId> deletedIds = doc.Delete(elementIdsToDelete);
                        DeletedCount = deletedIds.Count;

                        transaction.Commit();
                    }
                    IsSuccess = true;
                }
                else
                {
                    TaskDialog.Show("错误", "没有有效的元素可以删除");
                    IsSuccess = false;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("错误", "删除元素失败: " + ex.Message);
                IsSuccess = false;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }
        public string GetName()
        {
            return "删除元素";
        }
    }
}
