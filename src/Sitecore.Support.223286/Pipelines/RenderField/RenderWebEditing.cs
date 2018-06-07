using System.Collections.Specialized;
using System.Linq;
using Sitecore.Shell.Applications.ContentEditor;

namespace Sitecore.Support.Pipelines.RenderField
{
  using Sitecore;
  using Sitecore.Configuration;
  using Sitecore.Data;
  using Sitecore.Data.Fields;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Globalization;
  using Sitecore.Pipelines.GetChromeData;
  using Sitecore.Pipelines.RenderField;
  using Sitecore.Shell;
  using Sitecore.Sites;
  using Sitecore.StringExtensions;
  using Sitecore.Text;
  using Sitecore.Web;
  using Sitecore.Web.UI.HtmlControls;
  using Sitecore.Web.UI.PageModes;
  using Sitecore.Xml.Xsl;
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Web;
  using System.Web.UI;

  public class RenderWebEditing
  {
    private void AddParameters(Tag tag, RenderFieldArgs args)
    {
      Assert.ArgumentNotNull(tag, "tag");
      Assert.ArgumentNotNull(args, "args");
      #region Added code
      NameValueCollection parameters = Sitecore.Web.WebUtil.ParseParameters(args.RawParameters, '&');
      foreach (string key in Sitecore.Web.WebUtil.ParseParameters(args.RawParameters, '&'))
      {
        if (!args.WebEditParameters.Keys.Contains(key))
        {
          args.WebEditParameters.Add(key, parameters[key]);
        }
      }
      #endregion
      if (args.WebEditParameters.Count > 0)
      {
        UrlString str = new UrlString();
        foreach (KeyValuePair<string, string> pair in args.WebEditParameters)
        {
          str.Add(pair.Key, pair.Value);
        }
        tag.Add("sc_parameters", str.ToString());
      }
    }

    private static void ApplyWordFieldStyle(Tag tag, RenderFieldArgs args)
    {
      Assert.ArgumentNotNull(tag, "tag");
      Assert.ArgumentNotNull(args, "args");
      string str = args.Parameters["editorwidth"] ?? Settings.WordOCX.Width;
      string str2 = args.Parameters["editorheight"] ?? Settings.WordOCX.Height;
      string str3 = args.Parameters["editorpadding"] ?? Settings.WordOCX.Padding;
      str = str.ToLowerInvariant().Replace("px", string.Empty);
      int @int = MainUtil.GetInt(str, -1);
      str2 = str2.ToLowerInvariant().Replace("px", string.Empty);
      int num2 = MainUtil.GetInt(str2, -1);
      int num3 = MainUtil.GetInt(str3.ToLowerInvariant().Replace("px", string.Empty), -1);
      if (num3 >= 0)
      {
        if (@int >= 0)
        {
          str = (@int + (2 * num3)) + string.Empty;
        }
        if (num2 >= 0)
        {
          str2 = (num2 + (2 * num3)) + string.Empty;
        }
      }
      tag.Class = tag.Class + " scWordContainer";
      tag.Style = "width:{0}px;height:{1}px;padding:{2};".FormatWith(new object[] { str, str2, str3 });
    }

    private static bool CanEditField(Field field)
    {
      Assert.ArgumentNotNull(field, "field");
      if (!field.CanWrite)
      {
        return false;
      }
      return true;
    }

    private static bool CanEditItem(Item item)
    {
      Assert.ArgumentNotNull(item, "item");
      if ((!Context.IsAdministrator && item.Locking.IsLocked()) && !item.Locking.HasLock())
      {
        return false;
      }
      if (!item.Access.CanWrite())
      {
        return false;
      }
      if (!item.Access.CanWriteLanguage())
      {
        return false;
      }
      if (item.Appearance.ReadOnly)
      {
        return false;
      }
      return true;
    }

    public static bool CanRenderField(RenderFieldArgs args)
    {
      if (!CanWebEdit(args) && !args.WebEditParameters.ContainsKey("sc-highlight-contentchange"))
      {
        return false;
      }
      if (args.Item == null)
      {
        return false;
      }
      if (!CanEditItem(args.Item))
      {
        return false;
      }
      Field field = args.Item.Fields[args.FieldName];
      if (field == null)
      {
        return false;
      }
      if (!CanEditField(field))
      {
        return false;
      }
      return true;
    }

    private static bool CanWebEdit(RenderFieldArgs args)
    {
      if (args.DisableWebEdit)
      {
        return false;
      }
      SiteContext site = Context.Site;
      if (site == null)
      {
        return false;
      }
      if (site.DisplayMode != DisplayMode.Edit)
      {
        return false;
      }
      if (WebUtil.GetQueryString("sc_duration") == "temporary")
      {
        return false;
      }
      if (!Context.PageMode.IsExperienceEditorEditing)
      {
        return false;
      }
      return true;
    }

    private static Tag CreateFieldTag(string tagName, RenderFieldArgs args, string controlID)
    {
      Assert.ArgumentNotNull(tagName, "tagName");
      Assert.ArgumentNotNull(args, "args");
      Tag tag = new Tag(tagName)
      {
        ID = controlID + "_edit"
      };
      tag.Add("scFieldType", args.FieldTypeKey);
      return tag;
    }

    private static string GetDefaultText(RenderFieldArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      string str = StringUtil.GetString(new string[] { args.RenderParameters["default-text"], string.Empty });
      using (new LanguageSwitcher(WebUtil.GetCookieValue("shell", "lang", Context.Language.Name)))
      {
        if (str.IsNullOrEmpty())
        {
          Database database = Factory.GetDatabase("core");
          Assert.IsNotNull(database, "core");
          Item item = database.GetItem("/sitecore/content/Applications/WebEdit/WebEdit Texts");
          Assert.IsNotNull(item, "/sitecore/content/Applications/WebEdit/WebEdit Texts");
          str = item["Default Text"];
        }
        if (string.Compare(args.RenderParameters["show-title-when-blank"], "true", StringComparison.InvariantCultureIgnoreCase) == 0)
        {
          str = GetFieldDisplayName(args) + ": " + str;
        }
      }
      return str;
    }

    private string GetEditableElementTagName(RenderFieldArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      string str = "span";
      if ((UIUtil.IsFirefox() || UIUtil.IsWebkit()) && (UIUtil.SupportsInlineEditing() && MainUtil.GetBool(args.Parameters["block-content"], false)))
      {
        str = "div";
      }
      return str;
    }

    private static string GetFieldData(RenderFieldArgs args, Field field, string controlID)
    {
      Assert.ArgumentNotNull(args, "args");
      Assert.ArgumentNotNull(field, "field");
      Assert.ArgumentNotNull(controlID, "controlID");
      Item item = field.Item;
      Assert.IsNotNull(Context.Site, "site");
      using (new LanguageSwitcher(WebUtil.GetCookieValue("shell", "lang", Context.Site.Language)))
      {
        GetChromeDataArgs args2 = new GetChromeDataArgs("field", item, args.Parameters)
        {
          CustomData = { ["field"] = field }
        };
        GetChromeDataPipeline.Run(args2);
        ChromeData chromeData = args2.ChromeData;
        SetCommandParametersValue(chromeData.Commands, field, controlID);
        return chromeData.ToJson();
      }
    }

    private static string GetFieldDisplayName(RenderFieldArgs args)
    {
      Item item;
      Assert.IsNotNull(args, "args");
      Assert.IsNotNull(args.Item, "item");
      if (string.Compare(WebUtil.GetCookieValue("shell", "lang", Context.Language.Name), args.Item.Language.Name, StringComparison.InvariantCultureIgnoreCase) != 0)
      {
        item = args.Item.Database.GetItem(args.Item.ID);
        Assert.IsNotNull(item, "Item");
      }
      else
      {
        item = args.Item;
      }
      Field field = item.Fields[args.FieldName];
      if (field != null)
      {
        return field.DisplayName;
      }
      return args.FieldName;
    }

    private string GetRawValueContainer(Field field, string controlID)
    {
      Assert.ArgumentNotNull(field, "field");
      Assert.ArgumentNotNull(controlID, "controlID");
      return "<input id='{0}' class='scFieldValue' name='{0}' type='hidden' value=\"{1}\" />".FormatWith(new object[] { controlID, HttpUtility.HtmlEncode(field.Value) });
    }

    public void Process(RenderFieldArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      if (CanRenderField(args))
      {
        Field field = args.Item.Fields[args.FieldName];
        Item item = field.Item;
        string str = item[FieldIDs.Revision].Replace("-", string.Empty);
        string controlID = string.Concat(new object[] { "fld_", item.ID.ToShortID(), "_", field.ID.ToShortID(), "_", item.Language, "_", item.Version, "_", str, "_", MainUtil.GetSequencer() });
        HtmlTextWriter output = new HtmlTextWriter(new StringWriter());
        if (args.EnclosingTag.Length > 0)
        {
          output.Write("<{0}>", args.EnclosingTag);
        }
        string rawValueContainer = this.GetRawValueContainer(field, controlID);
        output.Write(rawValueContainer);
        if (args.DisableWebEditContentEditing && args.DisableWebEditFieldWrapping)
        {
          this.RenderWrapperlessField(output, args, field, controlID);
        }
        else
        {
          this.RenderWrappedField(output, args, field, controlID);
        }
      }
    }

    private void RenderWrappedField(HtmlTextWriter output, RenderFieldArgs args, Field field, string controlID)
    {
      Assert.ArgumentNotNull(output, "output");
      Assert.ArgumentNotNull(args, "args");
      Assert.ArgumentNotNull(controlID, "controlID");
      string str = GetFieldData(args, field, controlID);
      if (args.Before.Length > 0)
      {
        output.Write(args.Before);
      }
      output.Write("<span class=\"scChromeData\">{0}</span>", str);
      Tag tag = CreateFieldTag(this.GetEditableElementTagName(args), args, controlID);
      tag.Class = "scWebEditInput";
      if (!args.DisableWebEditContentEditing)
      {
        tag.Add("contenteditable", "true");
      }
      string firstPart = args.Result.FirstPart;
      string defaultText = GetDefaultText(args);
      tag.Add("scDefaultText", defaultText);
      if (string.IsNullOrEmpty(firstPart))
      {
        tag.Add("scWatermark", "true");
        firstPart = defaultText;
      }
      this.AddParameters(tag, args);
      if ((args.FieldTypeKey.ToLowerInvariant() == "word document") && (args.Parameters["editormode"] == "inline"))
      {
        ApplyWordFieldStyle(tag, args);
      }
      output.Write(tag.Start());
      output.Write(firstPart);
      args.Result.FirstPart = output.InnerWriter.ToString();
      string str5 = tag.End();
      if (args.After.Length > 0)
      {
        str5 = str5 + args.After;
      }
      if (args.EnclosingTag.Length > 0)
      {
        str5 = string.Format("{1}</{0}>", args.EnclosingTag, str5);
      }
      RenderFieldResult result = args.Result;
      result.LastPart = result.LastPart + str5;
    }

    private void RenderWrapperlessField(HtmlTextWriter output, RenderFieldArgs args, Field field, string controlID)
    {
      Assert.ArgumentNotNull(output, "output");
      Assert.ArgumentNotNull(args, "args");
      Assert.ArgumentNotNull(controlID, "controlID");
      Tag tag = CreateFieldTag("code", args, controlID);
      tag.Class = "scpm";
      tag.Add("kind", "open").Add("type", "text/sitecore").Add("chromeType", "field");
      string firstPart = args.Result.FirstPart;
      if (string.IsNullOrEmpty(firstPart))
      {
        tag.Add("scWatermark", "true");
        string defaultText = GetDefaultText(args);
        firstPart = defaultText;
        if (StringUtil.RemoveTags(defaultText) == defaultText)
        {
          firstPart = "<span class='scTextWrapper'>" + defaultText + "</span>";
        }
      }
      this.AddParameters(tag, args);
      string str3 = GetFieldData(args, field, controlID);
      tag.InnerHtml = str3;
      output.Write(tag.ToString());
      output.Write(firstPart);
      args.Result.FirstPart = output.InnerWriter.ToString();
      Tag tag2 = new Tag("code")
      {
        Class = "scpm"
      };
      tag2.Add("kind", "close").Add("type", "text/sitecore").Add("chromeType", "field");
      RenderFieldResult result = args.Result;
      result.LastPart = result.LastPart + tag2.ToString();
    }

    private static void SetCommandParametersValue(IEnumerable<WebEditButton> commands, Field field, string controlID)
    {
      string str;
      Assert.ArgumentNotNull(commands, "commands");
      Assert.ArgumentNotNull(field, "field");
      Assert.ArgumentNotNull(controlID, "controlID");
      Item item = field.Item;
      if (UserOptions.WebEdit.UsePopupContentEditor)
      {
        str = string.Concat(new object[] { "javascript:Sitecore.WebEdit.postRequest(\"webedit:edit(id=", item.ID, ",language=", item.Language, ",version=", item.Version, ")\")" });
      }
      else
      {
        str = new UrlString(WebUtil.GetRawUrl())
        {
          ["sc_ce"] = "1",
          ["sc_ce_uri"] = HttpUtility.UrlEncode(item.Uri.ToString())
        }.ToString();
      }
      foreach (WebEditButton button in commands)
      {
        if (!string.IsNullOrEmpty(button.Click))
        {
          string str3 = button.Click.Replace("$URL", str).Replace("$ItemID", item.ID.ToString()).Replace("$Language", item.Language.ToString()).Replace("$Version", item.Version.ToString()).Replace("$FieldID", field.ID.ToString()).Replace("$ControlID", controlID).Replace("$MessageParameters", string.Concat(new object[] { "itemid=", item.ID, ",language=", item.Language, ",version=", item.Version, ",fieldid=", field.ID, ",controlid=", controlID })).Replace("$JavascriptParameters", string.Concat(new object[] { "\"", item.ID, "\",\"", item.Language, "\",\"", item.Version, "\",\"", field.ID, "\",\"", controlID, "\"" }));
          button.Click = str3;
        }
      }
    }
  }
}