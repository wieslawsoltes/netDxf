"""Real ROTATE/SCALE Reference+Copy acquisition, history and independent DXF checks."""
import math
import ezdxf
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_transform_reference(page, output, state, command, wait_state, idle, check):
    original = page.locator('#studio').get_attribute('data-document')
    original_settings = page.evaluate('shadowStudioAgentUi.snapshot().settings')
    drawing = ezdxf.new('R2018')
    line = drawing.modelspace().add_line((10, 20, 0), (20, 20, 0))
    block = drawing.blocks.new('Reference block')
    block.add_line((0, 0), (5, 0))
    block.add_attdef('TAG', (1, 2), text='Default', dxfattribs={'height': 1})
    insert = drawing.modelspace().add_blockref('Reference block', (50, 50))
    insert.add_auto_attribs({'TAG': 'Custom value'})
    path = output / 'transform-reference-source.dxf'
    drawing.saveas(path)

    def select(handle):
        page.evaluate("id=>shadowStudioAgentUi.execute('cad.edit',{path:'select',body:{mode:'entities',entities:[{value:id}]}})",
                      'H:' + handle.upper())
        idle()

    def answer(value, prompt):
        field = page.locator('#command-input')
        field.fill(value); field.press('Enter')
        expect(page.locator('#prompt')).to_contain_text(prompt)
        idle()

    def saved(name):
        with page.expect_download() as pending:
            page.locator('[data-action="save"]').first.click()
        target = output / name
        pending.value.save_as(str(target)); idle()
        result = ezdxf.readfile(target); audit = result.audit()
        assert not audit.errors and not audit.fixes
        return result

    def near(actual, expected):
        return all(math.isclose(a, b, abs_tol=1e-7) for a, b in zip(actual, expected))

    if page.locator('#documents .document-tab').count() >= 8:
        spare = next((tab for tab in page.locator('#documents .document-tab:not(.active)').all()
                      if not tab.locator('.name').inner_text().startswith('● ')), None)
        assert spare is not None, 'Reference-transform fixture needs a saved document slot.'
        spare.locator('.close').click()
        expect(page.locator('#documents .document-tab')).to_have_count(7)
        idle()
    page.locator('#open-file').set_input_files(str(path))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', original)
    idle()
    fixture_id = page.locator('#studio').get_attribute('data-document')
    try:
        select(line.dxf.handle)
        before = state()['state']
        command('ROTATE 10,20 C R 0 90')
        after = state()
        check('ROTATE Copy Reference creates one selected independent root in one undo entry',
              after['state']['undoCount'] == before['undoCount'] + 1 and
              len(after['selectedEntities']) == 1 and after['selectedEntities'][0] != 'H:' + line.dxf.handle.upper())
        result = saved('rotate-reference-copy.dxf')
        original_line = result.entitydb[line.dxf.handle]
        rotated = next(item for item in result.modelspace().query('LINE') if item != original_line)
        check('independent DXF geometry retains the original and rotates its copy by target minus reference',
              near(original_line.dxf.end, (20, 20, 0)) and near(rotated.dxf.start, (10, 20, 0)) and near(rotated.dxf.end, (10, 30, 0)))
        command('U')
        select(line.dxf.handle)
        command('SCALE 10,20 R 10 25')
        result = saved('scale-reference.dxf')
        check('SCALE typed reference uses a length ratio about the selected base',
              near(result.entitydb[line.dxf.handle].dxf.end, (35, 20, 0)))
        command('U')
        select(line.dxf.handle)
        command('ROTATE 10,20 R 100,100 110,100 P 200,200 200,210')
        result = saved('rotate-reference-points.dxf')
        check('ROTATE independent reference and target pairs do not move the base point',
              near(result.entitydb[line.dxf.handle].dxf.start, (10, 20, 0)) and
              near(result.entitydb[line.dxf.handle].dxf.end, (10, 30, 0)))
        command('U')
        select(line.dxf.handle)
        page.evaluate("()=>shadowStudioAgentUi.execute('settings.set',{osnap:true,snapModes:1,snap:false,ortho:false,polar:false})")
        page.evaluate("()=>shadowStudioAgentUi.execute('ui.action',{name:'fit'})")
        idle()
        command('ROTATE 10,20 R 0', False)
        expect(page.locator('#prompt')).to_contain_text('new angle')
        camera = page.evaluate('shadowStudioAgentUi.snapshot().camera')
        box = page.locator('#drawing').bounding_box()
        # Pick the INSERT's exact line start via a running endpoint snap.
        page.mouse.click(box['x'] + box['width'] / 2 + (50 - camera['x']) * camera['scale'],
                         box['y'] + box['height'] / 2 - (50 - camera['y']) * camera['scale'])
        expect(page.locator('#prompt')).to_have_text('Command:')
        idle()
        result = saved('rotate-reference-picked-target.dxf')
        check('picked new-angle endpoint uses the base point and exact running object snap',
              near(result.entitydb[line.dxf.handle].dxf.end, (18, 26, 0)))
        command('U')
        select(line.dxf.handle)
        command('SCALE 10,20 C R 0,0,0 0,3,4 P 100,100,0 100,100,10')
        result = saved('scale-reference-copy-points.dxf')
        check('SCALE Copy accepts 3D reference and target lengths and retains source identity',
              len(list(result.modelspace().query('LINE'))) == 2 and
              near(result.entitydb[line.dxf.handle].dxf.end, (20, 20, 0)) and
              any(near(item.dxf.end, (30, 20, 0)) for item in result.modelspace().query('LINE')))
        command('U')
        select(line.dxf.handle)
        before = state()['state']
        command('SCALE 10,20 R', False)
        errors = page.locator('#history .error')
        error_count = errors.count()
        answer('-1', 'reference length')
        expect(errors).to_have_count(error_count + 1)
        expected_error = errors.nth(error_count)
        expect(expected_error).to_have_text('Specify a positive length or factor of at least 1e-9.')
        expected_error.evaluate("row=>row.dataset.testExpectedError='scale-reference-negative'")
        check('negative SCALE reference reports exactly one expected validation error')
        check('invalid negative reference length does not publish geometry or history',
              state()['state']['revision'] == before['revision'] and state()['state']['undoCount'] == before['undoCount'])
        answer('10,20', 'second reference length point')
        page.locator('#command-input').press('Escape')
        expect(page.locator('#prompt')).to_have_text('Command:')
        idle()
        check('Escape cancels partially acquired reference points without a source transaction',
              state()['state']['revision'] == before['revision'] and state()['state']['undoCount'] == before['undoCount'])
        select(insert.dxf.handle)
        command('ROTATE 50,50 C R 0 90')
        result = saved('rotate-reference-attributes.dxf')
        original_insert = result.entitydb[insert.dxf.handle]
        copy = next(item for item in result.modelspace().query('INSERT') if item != original_insert)
        check('reference-copy INSERT keeps its shared definition and independent transformed attribute value',
              copy.dxf.name == original_insert.dxf.name and copy.attribs[0].dxf.handle != original_insert.attribs[0].dxf.handle and
              copy.attribs[0].dxf.text == 'Custom value' and near(copy.attribs[0].dxf.insert, (48, 51, 0)) and
              near(original_insert.attribs[0].dxf.insert, (51, 52, 0)))
        command('U'); command('REDO')
        result = saved('rotate-reference-attributes-redo.dxf')
        check('reference-copy undo and redo retain attribute geometry in independently reloaded DXF',
              len(list(result.modelspace().query('INSERT'))) == 2 and
              any(near(item.attribs[0].dxf.insert, (48, 51, 0)) for item in result.modelspace().query('INSERT')))
        page.screenshot(path=str(output / 'transform-reference-workflow.png'))
    finally:
        page.locator('#command-input').press('Escape'); idle()
        page.evaluate("settings=>shadowStudioAgentUi.execute('settings.set',settings)",
                      {key: original_settings[key] for key in ('osnap', 'snapModes', 'snap', 'ortho', 'polar') if key in original_settings})
        if page.locator('#studio').get_attribute('data-document') == fixture_id:
            page.locator('#documents .document-tab.active .close').click()
            wait_for_ui(page, "id=>document.querySelector('#studio').dataset.document!==id||document.querySelector('#dialog')?.open", arg=fixture_id)
            if page.locator('#dialog').evaluate('dialog=>dialog.open'):
                page.locator('#dialog-submit').click()
            expect(page.locator('#studio')).not_to_have_attribute('data-document', fixture_id)
        page.evaluate("id=>shadowStudioAgentUi.execute('document.activate',{id})", original)
        expect(page.locator('#studio')).to_have_attribute('data-document', original)
        idle()
