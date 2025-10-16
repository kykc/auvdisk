<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    
    <id>{AUTOMATL_ID}</id>
    
    <version>{AUTOMATL_VERSION}</version>
    
    <title>{AUTOMATL_TITLE}</title>
    <authors>{AUTOMATL_AUTHORS_COMMA_SEPARATED}</authors>
    <projectUrl>{AUTOMATL_URL}</projectUrl>
    
    <tags>{AUTOMATL_TAGS_SPACE_SEPARATED}</tags>
    <summary>{AUTOMATL_SUMMARY}</summary>
    <description>{AUTOMATL_DESCRIPTION_MARKDOWN}</description>

  </metadata>
  <files>
    <!-- this section controls what actually gets packaged into the Chocolatey package -->
    <file src="{AUTOMATL_TARGET_DIR}**" target="tools" />
  </files>
</package>