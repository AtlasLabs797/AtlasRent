import { Button } from '../design-system/components/core/Button';
import { EmptyState } from '../design-system/components/feedback/EmptyState';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { textos } from '../i18n/textos';

export default function NoEncontrada() {
  return (
    <>
      <PageHeader eyebrow={textos.app.nombre} title={textos.app.noEncontradaTitulo} description={textos.app.noEncontradaCuerpo} />
      <EmptyState icon="search-x" title={textos.app.noEncontradaTitulo} action={<Button>{textos.app.irAlInicio}</Button>}>
        {textos.app.noEncontradaCuerpo}
      </EmptyState>
    </>
  );
}