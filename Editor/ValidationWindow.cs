using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Fsi.Validation
{
	// TODO - UNITY_2019_1_OR_NEWER for package - Kira
	public sealed class ValidationWindow : EditorWindow
	{
		[FormerlySerializedAs("styleSheet")]
		[SerializeField]
		private StyleSheet stylesheet;
		
		private readonly List<MethodInfo> methods = new();
		private Vector2 sidebarScroll;
		private Vector2 detailsScroll;
		private int selectedIndex = -1;
		private ValidatorResult? lastResult;
		private string lastStatusMessage;
		private string lastException;
		private string runAllSummary;
		private string runAllDetails;
		private bool runAllHasIssues;
		
		// Elements
		private ListView listView;
		private HelpBox selectionHelpBox;
		private VisualElement detailsContainer;
		private Label methodNameValue;
		private Label declaringTypeValue;
		private Label returnTypeValue;
		private Label attributeValue;
		private Button runButton;
		private Button runAllButton;
		private VisualElement resultContainer;
		private Label resultStatusLabel;
		private Label resultMessageLabel;
		private TextField resultExceptionField;
		private VisualElement runAllContainer;
		private Label runAllSummaryLabel;
		private TextField runAllDetailsField;
		
		[MenuItem("FSI/Validation Window")]
		public static void ShowWindow()
		{
			ValidationWindow window = GetWindow<ValidationWindow>("Validation Window");
			window.minSize = new Vector2(720f, 420f);
			window.RefreshMethods();
		}

		private void OnEnable()
		{
			RefreshMethods();
		}

		private void RefreshMethods()
		{
			ValidatorRegistry.ClearCache();
			methods.Clear();
			IReadOnlyList<MethodInfo> discovered = ValidatorRegistry.GetValidatorMethods();
			if (discovered != null)
			{
				methods.AddRange(discovered);
				methods.Sort((a, b) =>
				             {
					             string aLabel = $"{a.DeclaringType?.Name}.{a.Name}";
					             string bLabel = $"{b.DeclaringType?.Name}.{b.Name}";
					             return string.Compare(aLabel, bLabel, StringComparison.OrdinalIgnoreCase);
				             });
			}

			if (selectedIndex >= methods.Count)
			{
				selectedIndex = methods.Count - 1;
			}

			lastResult = null;
			lastStatusMessage = null;
			lastException = null;
			runAllSummary = null;
			runAllDetails = null;
			runAllHasIssues = false;
			
			Repaint();
            
			RefreshListView();
			UpdateDetailsUI();
			UpdateResultUI();
			UpdateRunAllUI();
		}
		
		private void CreateGUI()
		{
			VisualElement root = rootVisualElement;
			root.Clear();
			root.AddToClassList("validator-root");
			root.styleSheets.Add(stylesheet);

			VisualElement sidebar = new();
			sidebar.AddToClassList("validator-sidebar");

			Toolbar toolbar = new();
			Label toolbarLabel = new("Validation Methods");
			toolbarLabel.AddToClassList("validator-toolbar-label");
			toolbar.Add(toolbarLabel);
			toolbar.Add(new ToolbarSpacer());
			ToolbarButton refreshButton = new(RefreshMethods) { text = "Refresh" };
			refreshButton.AddToClassList("validator-toolbar-button");
			toolbar.Add(refreshButton);
			sidebar.Add(toolbar);

			listView = new ListView(methods, 20f, () => new Label(), (element, index) =>
			                                                         {
				                                                         Label label = (Label)element;
				                                                         label.AddToClassList("validator-list-item");
				                                                         label.text
					                                                         = index >= 0 && index < methods.Count
						                                                           ? GetMethodLabel(methods[index])
						                                                           : string.Empty;
			                                                         })
			           {
				           selectionType = SelectionType.Single,
			           };
			listView.AddToClassList("validator-list");
			listView.selectionChanged += _ =>
			                             {
				                             SetSelectedIndex(listView.selectedIndex);
				                             UpdateDetailsUI();
				                             UpdateResultUI();
			                             };
			sidebar.Add(listView);

			VisualElement detailsPanel = new();
			detailsPanel.AddToClassList("validator-details-panel");

			ScrollView detailsScrollView = new();
			detailsScrollView.AddToClassList("validator-details-scroll");

			selectionHelpBox = new HelpBox("Select a validator method to see details.", HelpBoxMessageType.Info);
			selectionHelpBox.AddToClassList("validator-selection-help");
			detailsScrollView.Add(selectionHelpBox);

			detailsContainer = new VisualElement();
			detailsContainer.AddToClassList("validator-details-container");

			methodNameValue = AddDetailRow(detailsContainer, "Method Name");
			declaringTypeValue = AddDetailRow(detailsContainer, "Declaring Type");
			returnTypeValue = AddDetailRow(detailsContainer, "Return Type");
			attributeValue = AddDetailRow(detailsContainer, "Validator Attribute");
			detailsScrollView.Add(detailsContainer);

			VisualElement buttonRow = new();
			buttonRow.AddToClassList("validator-button-row");

			runButton = new Button(clickEvent: () =>
			                                   {
				                                   MethodInfo method = GetSelectedMethod();
				                                   if (method != null)
				                                   {
					                                   RunValidator(method);
					                                   UpdateResultUI();
				                                   }
			                                   })
			            {
				            text = "Run",
			            };
			runButton.AddToClassList("validator-button");

			runAllButton = new Button(clickEvent: () =>
			                                      {
				                                      RunAllValidators();
				                                      UpdateRunAllUI();
			                                      })
			               {
				               text = "Run All",
			               };
			runAllButton.AddToClassList("validator-button");

			buttonRow.Add(runButton);
			buttonRow.Add(runAllButton);
			sidebar.Add(buttonRow);

			resultContainer = new VisualElement();
			resultContainer.AddToClassList("validator-result");

			resultStatusLabel = new Label();
			resultStatusLabel.AddToClassList("validator-result-status");
			resultMessageLabel = new Label();
			resultMessageLabel.AddToClassList("validator-result-message");
			resultExceptionField = new TextField("Error Details")
			                       {
				                       multiline = true,
				                       isReadOnly = true,
			                       };
			resultExceptionField.AddToClassList("validator-result-exception");

			resultContainer.Add(resultStatusLabel);
			resultContainer.Add(resultMessageLabel);
			resultContainer.Add(resultExceptionField);
			detailsScrollView.Add(resultContainer);

			runAllContainer = new VisualElement();
			runAllContainer.AddToClassList("validator-runall");

			runAllSummaryLabel = new Label();
			runAllSummaryLabel.AddToClassList("validator-runall-summary");
			runAllDetailsField = new TextField
			                     {
				                     multiline = true,
				                     isReadOnly = true,
			                     };
			runAllDetailsField.AddToClassList("validator-runall-details");

			runAllContainer.Add(runAllSummaryLabel);
			runAllContainer.Add(runAllDetailsField);
			detailsScrollView.Add(runAllContainer);

			detailsPanel.Add(detailsScrollView);

			root.Add(sidebar);
			root.Add(detailsPanel);

			RefreshListView();
			UpdateDetailsUI();
			UpdateResultUI();
			UpdateRunAllUI();
		}
		
		private void SetSelectedIndex(int index)
		{
			selectedIndex = index;
			lastResult = null;
			lastStatusMessage = null;
			lastException = null;
		}
		
		private MethodInfo GetSelectedMethod()
		{
			return selectedIndex >= 0 && selectedIndex < methods.Count ? methods[selectedIndex] : null;
		}

		#if UNITY_2019_1_OR_NEWER
	private static Label AddDetailRow(VisualElement parent, string labelText)
	{
		VisualElement row = new();
		row.AddToClassList("validator-detail-row");

		Label label = new(labelText);
		label.AddToClassList("validator-detail-label");

		Label value = new();
		value.AddToClassList("validator-detail-value");

		row.Add(label);
		row.Add(value);
		parent.Add(row);

			return value;
		}

		private void RefreshListView()
		{
			if (listView == null)
			{
				return;
			}

			listView.itemsSource = methods;
			listView.Rebuild();
			listView.selectedIndex = selectedIndex;
		}

		private void UpdateDetailsUI()
		{
			if (selectionHelpBox == null || detailsContainer == null)
			{
				return;
			}

			MethodInfo method = GetSelectedMethod();
			bool hasSelection = method != null;
			selectionHelpBox.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
			detailsContainer.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;

			runButton?.SetEnabled(hasSelection);
			runAllButton?.SetEnabled(methods.Count > 0);

			if (hasSelection)
			{
				bool hasAttribute = method.GetCustomAttribute<ValidationMethod>() != null;
				methodNameValue.text = method.Name;
				declaringTypeValue.text = method.DeclaringType?.FullName ?? "Unknown";
				returnTypeValue.text = method.ReturnType.Name;
				attributeValue.text = hasAttribute ? "Yes" : "No";
			}
			else
			{
				methodNameValue.text = "—";
				declaringTypeValue.text = "—";
				returnTypeValue.text = "—";
				attributeValue.text = "—";
			}
		}

		private void UpdateResultUI()
		{
			if (resultContainer == null)
			{
				return;
			}

			bool show = lastResult.HasValue 
			            || !string.IsNullOrEmpty(lastStatusMessage) 
			            || !string.IsNullOrEmpty(lastException);
			resultContainer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
			if (!show)
			{
				return;
			}

			bool passed = lastResult is { Passed: true };
			resultContainer.EnableInClassList("result--passed", passed);
			resultContainer.EnableInClassList("result--failed", !passed);

			resultStatusLabel.text = $"Status: {(passed ? "Passed" : "Failed")}";
			string message = string.IsNullOrWhiteSpace(lastStatusMessage) ? "None" : lastStatusMessage;
			resultMessageLabel.text = $"Message: {message}";

			if (!string.IsNullOrWhiteSpace(lastException))
			{
				resultExceptionField.style.display = DisplayStyle.Flex;
				resultExceptionField.value = lastException;
			}
			else
			{
				resultExceptionField.style.display = DisplayStyle.None;
				resultExceptionField.value = string.Empty;
			}
		}

		private void UpdateRunAllUI()
		{
			if (runAllContainer == null)
			{
				return;
			}

			bool show = !string.IsNullOrEmpty(runAllSummary);
			runAllContainer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
			if (!show)
			{
				return;
			}

			runAllContainer.EnableInClassList("runall--failed", runAllHasIssues);
			runAllContainer.EnableInClassList("runall--passed", !runAllHasIssues);

			runAllSummaryLabel.text = $"Run All Summary: {runAllSummary}";
			runAllDetailsField.value = runAllDetails ?? string.Empty;
		}
		#endif

		// ReSharper disable Unity.PerformanceAnalysis
		private void RunValidator(MethodInfo method)
		{
			lastException = null;
			try
			{
				if (ValidatorRegistry.TryRunValidator(method, out ValidatorResult result))
				{
					lastResult = result;
					lastStatusMessage = string.IsNullOrWhiteSpace(result.Message)
						                    ? (result.Passed ? "Validator passed." : "Validator failed.")
						                    : result.Message;
				}
				else
				{
					lastResult = ValidatorResult.Fail("Invalid validator signature.");
					lastStatusMessage = "Validator could not run.";
					lastException = lastResult.Value.Message;
				}
			}
			catch (Exception ex)
			{
				lastResult = ValidatorResult.Fail("Exception thrown while running validator.");
				lastStatusMessage = lastResult.Value.Message;
				lastException = ex.ToString();
				Debug.LogError(ex);
			}
		}

		private void RunAllValidators()
		{
			int total = 0;
			int passed = 0;
			int failed = 0;
			List<string> lines = new();

			IReadOnlyList<MethodInfo> validators = ValidatorRegistry.GetValidatorMethods();
			foreach (MethodInfo method in validators)
			{
				total++;
				try
				{
					if (ValidatorRegistry.TryRunValidator(method, out ValidatorResult result))
					{
						if (result.Passed)
						{
							passed++;
						}
						else
						{
							failed++;
						}

						string message = string.IsNullOrWhiteSpace(result.Message) ? "No message." : result.Message;
						lines.Add($"{GetMethodLabel(method)}: {(result.Passed ? "Passed" : "Failed")} - {message}");
					}
					else
					{
						failed++;
						lines.Add($"{GetMethodLabel(method)}: Invalid validator signature.");
					}
				}
				catch (Exception ex)
				{
					failed++;
					lines.Add($"{GetMethodLabel(method)}: Exception - {ex.Message}");
				}
			}

			runAllSummary = $"Total: {total}  Passed: {passed}  Failed: {failed}";
			runAllDetails = lines.Count > 0 ? string.Join("\n", lines) : "No validators found.";
			runAllHasIssues = failed > 0;
		}

		private static string GetMethodLabel(MethodInfo method)
		{
			return $"{method.DeclaringType?.Name}.{method.Name}";
		}
	}
}
