"""Reviewable library import/export against the shared native and WASM UI."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_action


def verify_block_library(page, output, state, command, wait_state, idle, check):
    drawing = ezdxf.new('R2018'); drawing.appids.new('LIBRARY_META')
    child = drawing.blocks.new('LibraryMotor'); child.add_circle((0, 0), 8)
    root = drawing.blocks.new('LibraryPump', base_point=(5, 10, 0))
    root.block_record.dxf.units = 4
    root.add_blockref('LibraryMotor', (15, 20))
    root.add_attdef('TAG', (0, 0), text='LIB-101', dxfattribs={'height': 3})
    root.endblk.set_xdata('LIBRARY_META', [(1000, 'library terminator')])
    drawing.modelspace().add_blockref('LibraryPump', (5, 10)).add_auto_attribs({})
    seed = output / 'block-library-source.dxf'; drawing.saveas(seed)
    before = state()['state']
    ribbon_action(page, 'block-library-import', 'Insert', 'Blocks')
    expect(page.locator('#dialog-title')).to_have_text('Open block library')
    page.get_by_label('Block library DXF', exact=True).set_input_files(str(seed))
    page.locator('#dialog-submit').click()
    expect(page.locator('#dialog-title')).to_have_text('Import block library')
    page.get_by_label('Library definition', exact=True).select_option('LibraryPump')
    page.get_by_label('Imported block name', exact=True).fill('ImportedPump')
    page.get_by_label('Existing resource policy', exact=True).select_option('1')
    page.locator('#dialog-submit').click()
    expect(page.locator('#dialog')).not_to_be_visible()
    preview = page.locator('.analysis-preview'); expect(preview).to_have_count(1)
    expect(preview.locator('img')).to_have_count(3)
    expect(preview).to_contain_text('ImportedPump: resource decisions')
    check('library browsing and definition previews leave geometry and history unchanged',
          state()['state']['revision'] == before['revision'] and state()['state']['undoCount'] == before['undoCount'])
    page.screenshot(path=str(output / 'block-library-preview.png'))
    preview.get_by_role('button', name='Apply as one edit', exact=True).click()
    wait_state(lambda data: data['state']['revision'] > before['revision']); idle()
    imported = next(b for b in state()['blocks'] if b['name'] == 'ImportedPump')
    check('library definition imports as one undo operation without an implicit placement',
          state()['state']['undoCount'] == before['undoCount'] + 1 and state()['state']['occurrences'] == before['occurrences'])
    command('U'); check('undo removes the imported definition', not any(b['name'] == 'ImportedPump' for b in state()['blocks']))
    command('REDO'); check('redo retains the imported block identity', next(b['id'] for b in state()['blocks'] if b['name'] == 'ImportedPump') == imported['id'])
    export_before = state()['state']
    ribbon_action(page, 'block-library-export', 'Insert', 'Blocks')
    expect(page.locator('#dialog-title')).to_have_text('Export block library')
    page.get_by_label('Export block definition', exact=True).select_option(imported['id'])
    page.get_by_label('Binary block library DXF', exact=True).check()
    with page.expect_download() as download:
        page.locator('#dialog-submit').click()
    saved = output / 'exported-block-library.dxf'; download.value.save_as(str(saved)); idle()
    exported = ezdxf.readfile(saved); audit = exported.audit(); definition = exported.blocks['ImportedPump']
    check('independent library DXF audit preserves attributes units base point and ENDBLK metadata',
          not audit.errors and not audit.fixes and definition.block.dxf.base_point.isclose((5, 10, 0)) and
          definition.block_record.dxf.units == 4 and definition.get_attdef('TAG').dxf.text == 'LIB-101' and
          definition.endblk.get_xdata('LIBRARY_META')[0].value == 'library terminator')
    check('library export leaves the active drawing revision and history unchanged',
          state()['state']['revision'] == export_before['revision'] and state()['state']['undoCount'] == export_before['undoCount'])
    libraries = page.evaluate('''async () => shadowStudioAgentUi.execute('cad.read',{path:'block-libraries'},document.getElementById('studio').dataset.document)''')
    check('agents can discover loaded libraries without copying their DXF payload into context',
          len(libraries) == 1 and any(e['name'] == 'LibraryPump' for e in libraries[0]['entries']) and 'contentBase64' not in libraries[0])
    proposal = page.evaluate('''async libraryId => shadowStudioAgentUi.execute('cad.edit',{path:'edit-preview',body:{description:'Import a renamed library variant',operations:[
      {kind:'block-library',blockLibrary:{libraryId,sourceName:'LibraryPump',name:'ImportedPump',blockConflict:2,resourceConflict:1}}]}},document.getElementById('studio').dataset.document)''', libraries[0]['libraryId'])
    page.evaluate('''async previewId => shadowStudioAgentUi.execute('cad.edit',{path:'edit-preview/apply',body:{previewId}},document.getElementById('studio').dataset.document)''', proposal['previewId'])
    idle()
    check('agent library rename policy preserves the existing definition',
          any(b['name'] == 'ImportedPump$1' for b in state()['blocks']) and next(b['id'] for b in state()['blocks'] if b['name'] == 'ImportedPump') == imported['id'])
